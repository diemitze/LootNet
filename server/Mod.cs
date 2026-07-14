#nullable enable
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LootNetServer;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.20fpsguy.LootNet";
    public override string Name { get; init; } = "LootNet";
    public override string Author { get; init; } = "20fpsguy";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.9");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "";
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

public class RaidSummarySubmitRequest : IRequestData
{
    [System.Text.Json.Serialization.JsonPropertyName("groupId")]
    public string? GroupId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("playerId")]
    public string? PlayerId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("payload")]
    public string? Payload { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("expectedMembers")]
    public int ExpectedMembers { get; set; }
}

public class RaidSummaryListRequest : IRequestData
{
    [System.Text.Json.Serialization.JsonPropertyName("groupId")]
    public string? GroupId { get; set; }
}

// Persistent, cross-raid high-score records. Individual best is per player (team raids only);
// team best is per stable member-set. Written to disk next to the mod so it survives restarts.
public static class HighScoreStore
{
    public sealed class IndividualRecord
    {
        public double Value { get; set; }
        public string PlayerName { get; set; } = "";
        public string MapName { get; set; } = "";
        public long Timestamp { get; set; }
    }

    public sealed class TeamRecord
    {
        public double Value { get; set; }
        public List<string> Members { get; set; } = new();
        public long Timestamp { get; set; }
    }

    private sealed class PersistModel
    {
        public Dictionary<string, IndividualRecord> Individual { get; set; } = new();
        public Dictionary<string, TeamRecord> Team { get; set; } = new();
    }

    private static readonly object Lock = new();
    private static PersistModel _data = new();
    private static bool _loaded;

    private static string FilePath
    {
        get
        {
            var dir = System.IO.Path.GetDirectoryName(typeof(HighScoreStore).Assembly.Location)
                      ?? System.AppContext.BaseDirectory;
            return System.IO.Path.Combine(dir, "lootnet_highscores.json");
        }
    }

    private static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var json = System.IO.File.ReadAllText(FilePath);
                var model = JsonSerializer.Deserialize<PersistModel>(json);
                if (model != null) _data = model;
            }
        }
        catch { /* start fresh on any read/parse error */ }
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(FilePath, json);
        }
        catch { /* non-fatal: records just won't persist this write */ }
    }

    public static double GetIndividual(string playerId)
    {
        lock (Lock)
        {
            EnsureLoaded();
            return _data.Individual.TryGetValue(playerId, out var r) ? r.Value : 0;
        }
    }

    // Updates the stored record to max(existing, value). Returns the record value as it was BEFORE this call.
    public static double UpdateIndividual(string playerId, double value, string playerName, string mapName)
    {
        lock (Lock)
        {
            EnsureLoaded();
            _data.Individual.TryGetValue(playerId, out var existing);
            double prev = existing?.Value ?? 0;
            if (value > prev)
            {
                _data.Individual[playerId] = new IndividualRecord
                {
                    Value = value, PlayerName = playerName, MapName = mapName, Timestamp = Now,
                };
                Save();
            }
            return prev;
        }
    }

    public static double GetTeam(string teamKey)
    {
        lock (Lock)
        {
            EnsureLoaded();
            return _data.Team.TryGetValue(teamKey, out var r) ? r.Value : 0;
        }
    }

    public static double UpdateTeam(string teamKey, double value, List<string> members)
    {
        lock (Lock)
        {
            EnsureLoaded();
            _data.Team.TryGetValue(teamKey, out var existing);
            double prev = existing?.Value ?? 0;
            if (value > prev)
            {
                _data.Team[teamKey] = new TeamRecord { Value = value, Members = members, Timestamp = Now };
                Save();
            }
            return prev;
        }
    }
}

public static class RaidSummaryAggregator
{
    private sealed class Entry
    {
        public string Payload = "";
        public double Value;
        public long Timestamp;
        public double PrevIndividualRecord;
        public bool IndividualIsNew;
    }

    private sealed class GroupState
    {
        public readonly Dictionary<string, Entry> Members = new();
        public int ExpectedMembers;

        // Team record snapshot for THIS raid, captured once the team is complete.
        public bool TeamEvaluated;
        public double TeamPrevRecord;
        public double TeamLiveTotal;
        public bool TeamIsNew;
    }

    private const long TtlSeconds = 1800;
    private static readonly object Lock = new();
    private static readonly Dictionary<string, GroupState> Groups = new();

    private static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void Submit(string groupId, string playerId, string payload, int expectedMembers)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(playerId)) return;

        double value = 0;
        string playerName = "";
        string mapName = "";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("TotalFoundValue", out var v)) value = v.GetDouble();
            if (root.TryGetProperty("PlayerName", out var n)) playerName = n.GetString() ?? "";
            if (root.TryGetProperty("MapName", out var m)) mapName = m.GetString() ?? "";
        }
        catch { /* payload unreadable — record stays at 0, still relayed */ }

        lock (Lock)
        {
            Prune();
            if (!Groups.TryGetValue(groupId, out var group))
            {
                group = new GroupState();
                Groups[groupId] = group;
            }
            if (expectedMembers > group.ExpectedMembers) group.ExpectedMembers = expectedMembers;

            // A player may submit twice in one raid (extract + end-of-raid screen). Snapshot the
            // pre-raid record only on the first submission so a re-submit can't flip isNew back off.
            double prevIndividual;
            bool isNew;
            if (group.Members.TryGetValue(playerId, out var existing))
            {
                prevIndividual = existing.PrevIndividualRecord;
                isNew = existing.IndividualIsNew || value > prevIndividual;
                HighScoreStore.UpdateIndividual(playerId, value, playerName, mapName);
            }
            else
            {
                prevIndividual = HighScoreStore.UpdateIndividual(playerId, value, playerName, mapName);
                isNew = value > prevIndividual;
            }

            group.Members[playerId] = new Entry
            {
                Payload = payload ?? "",
                Value = value,
                Timestamp = Now,
                PrevIndividualRecord = prevIndividual,
                IndividualIsNew = isNew,
            };

            EvaluateTeam(group);
        }
    }

    // Once the full expected team has submitted, compute the combined haul and compare against the
    // persistent team record for this exact member set. Snapshot the pre-raid record so every client
    // that later reads the list sees the same "record to beat" and celebrates consistently.
    private static void EvaluateTeam(GroupState group)
    {
        if (group.TeamEvaluated) return;
        if (group.ExpectedMembers < 2) return;
        if (group.Members.Count < group.ExpectedMembers) return;

        var ids = new List<string>(group.Members.Keys);
        ids.Sort(System.StringComparer.Ordinal);
        string teamKey = string.Join("|", ids);

        double total = 0;
        foreach (var e in group.Members.Values) total += e.Value;

        double prev = HighScoreStore.UpdateTeam(teamKey, total, ids);

        group.TeamEvaluated = true;
        group.TeamPrevRecord = prev;
        group.TeamLiveTotal = total;
        group.TeamIsNew = total > prev;
    }

    public static string BuildListJson(string groupId, string? excludePlayerId)
    {
        var root = new JsonObject();
        var summaries = new JsonArray();
        var records = new JsonObject();
        var team = new JsonObject { ["r"] = 0.0, ["l"] = 0.0, ["n"] = false };

        if (!string.IsNullOrEmpty(groupId))
        {
            lock (Lock)
            {
                Prune();
                if (Groups.TryGetValue(groupId, out var group))
                {
                    foreach (var kvp in group.Members)
                    {
                        // Records for ALL members (incl. self) so everyone sees their own marker.
                        records[kvp.Key] = new JsonObject
                        {
                            ["r"] = kvp.Value.PrevIndividualRecord,
                            ["n"] = kvp.Value.IndividualIsNew,
                        };

                        // Summaries exclude self — the client already has its own RaidStats.
                        if (excludePlayerId != null && kvp.Key == excludePlayerId) continue;
                        if (string.IsNullOrEmpty(kvp.Value.Payload)) continue;
                        try { summaries.Add(JsonNode.Parse(kvp.Value.Payload)); }
                        catch { /* skip malformed */ }
                    }

                    if (group.TeamEvaluated)
                    {
                        team["r"] = group.TeamPrevRecord;
                        team["l"] = group.TeamLiveTotal;
                        team["n"] = group.TeamIsNew;
                    }
                }
            }
        }

        root["summaries"] = summaries;
        root["records"] = records;
        root["team"] = team;
        return root.ToJsonString();
    }

    private static void Prune()
    {
        long cutoff = Now - TtlSeconds;
        var emptyGroups = new List<string>();
        foreach (var group in Groups)
        {
            var stale = new List<string>();
            foreach (var member in group.Value.Members)
                if (member.Value.Timestamp < cutoff) stale.Add(member.Key);
            foreach (var key in stale) group.Value.Members.Remove(key);
            if (group.Value.Members.Count == 0) emptyGroups.Add(group.Key);
        }
        foreach (var key in emptyGroups) Groups.Remove(key);
    }
}

[Injectable]
public class LootNetRouter(JsonUtil jsonUtil, LootNetCallback callback) : StaticRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/lootnet/prices",
        async (url, info, sessionId, output) => await callback.HandleGetPrices(url, info, sessionId)
    ),
    new RouteAction<RaidSummarySubmitRequest>(
        "/lootnet/raidsummary/submit",
        async (url, info, sessionId, output) => await callback.HandleSubmitSummary(url, info, sessionId)
    ),
    new RouteAction<RaidSummaryListRequest>(
        "/lootnet/raidsummary/list",
        async (url, info, sessionId, output) => await callback.HandleListSummaries(url, info, sessionId)
    )
])
{ }

[Injectable]
public class LootNetCallback(
    HttpResponseUtil httpResponseUtil,
    RagfairPriceService ragfairPriceService,
    RagfairOfferService ragfairOfferService)
{
    public ValueTask<string> HandleGetPrices(string url, EmptyRequestData info, MongoId sessionId)
    {
        // Base table as a fallback for items with no live offer.
        var prices = new Dictionary<string, double>();
        foreach (var kvp in ragfairPriceService.GetAllFleaPrices())
            prices[kvp.Key.ToString()] = kvp.Value;

        // Prefer live flea offers so custom/inflated flea prices (e.g. SVM) are picked up.
        // RequirementsCost is the per-item rouble price; only generated (FakePlayer) offers count.
        var sums = new Dictionary<string, double>();
        var counts = new Dictionary<string, int>();
        foreach (var offer in ragfairOfferService.GetOffers())
        {
            if (offer.CreatedBy != OfferCreator.FakePlayer) continue;
            var items = offer.Items;
            if (items == null || items.Count == 0) continue;
            double cost = offer.RequirementsCost ?? 0;
            if (cost <= 0) continue;

            string tpl = items[0].Template.ToString();
            sums.TryGetValue(tpl, out double s);
            counts.TryGetValue(tpl, out int c);
            sums[tpl] = s + cost;
            counts[tpl] = c + 1;
        }
        foreach (var kvp in sums)
            prices[kvp.Key] = kvp.Value / counts[kvp.Key];

        return new ValueTask<string>(httpResponseUtil.GetBody(JsonSerializer.Serialize(prices)));
    }

    public ValueTask<string> HandleSubmitSummary(string url, RaidSummarySubmitRequest info, MongoId sessionId)
    {
        var playerId = string.IsNullOrEmpty(info.PlayerId) ? sessionId.ToString() : info.PlayerId!;
        RaidSummaryAggregator.Submit(info.GroupId ?? "", playerId, info.Payload ?? "", info.ExpectedMembers);
        return new ValueTask<string>(httpResponseUtil.GetBody("ok"));
    }

    public ValueTask<string> HandleListSummaries(string url, RaidSummaryListRequest info, MongoId sessionId)
    {
        var json = RaidSummaryAggregator.BuildListJson(info.GroupId ?? "", sessionId.ToString());
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }
}
