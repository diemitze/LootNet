#nullable enable
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Generic;
using System.Text.Json;

namespace LootNetServer;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.20fpsguy.LootNet";
    public override string Name { get; init; } = "LootNet";
    public override string Author { get; init; } = "20fpsguy";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.8");
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
}

public class RaidSummaryListRequest : IRequestData
{
    [System.Text.Json.Serialization.JsonPropertyName("groupId")]
    public string? GroupId { get; set; }
}

public static class RaidSummaryAggregator
{
    private sealed class Entry
    {
        public string Payload = "";
        public long Timestamp;
    }

    private const long TtlSeconds = 1800;
    private static readonly object Lock = new();
    private static readonly Dictionary<string, Dictionary<string, Entry>> Groups = new();

    private static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void Submit(string groupId, string playerId, string payload)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(playerId)) return;
        lock (Lock)
        {
            Prune();
            if (!Groups.TryGetValue(groupId, out var members))
            {
                members = new Dictionary<string, Entry>();
                Groups[groupId] = members;
            }
            members[playerId] = new Entry { Payload = payload ?? "", Timestamp = Now };
        }
    }

    public static List<string> List(string groupId, string? excludePlayerId)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(groupId)) return result;
        lock (Lock)
        {
            Prune();
            if (!Groups.TryGetValue(groupId, out var members)) return result;
            foreach (var kvp in members)
            {
                if (excludePlayerId != null && kvp.Key == excludePlayerId) continue;
                if (!string.IsNullOrEmpty(kvp.Value.Payload)) result.Add(kvp.Value.Payload);
            }
        }
        return result;
    }

    private static void Prune()
    {
        long cutoff = Now - TtlSeconds;
        var emptyGroups = new List<string>();
        foreach (var group in Groups)
        {
            var stale = new List<string>();
            foreach (var member in group.Value)
                if (member.Value.Timestamp < cutoff) stale.Add(member.Key);
            foreach (var key in stale) group.Value.Remove(key);
            if (group.Value.Count == 0) emptyGroups.Add(group.Key);
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
    RagfairPriceService ragfairPriceService)
{
    public ValueTask<string> HandleGetPrices(string url, EmptyRequestData info, MongoId sessionId)
    {
        var prices = ragfairPriceService.GetAllFleaPrices();
        var stringPrices = new Dictionary<string, double>();
        foreach (var kvp in prices)
            stringPrices[kvp.Key.ToString()] = kvp.Value;
        return new ValueTask<string>(httpResponseUtil.GetBody(JsonSerializer.Serialize(stringPrices)));
    }

    public ValueTask<string> HandleSubmitSummary(string url, RaidSummarySubmitRequest info, MongoId sessionId)
    {

        var playerId = string.IsNullOrEmpty(info.PlayerId) ? sessionId.ToString() : info.PlayerId!;
        RaidSummaryAggregator.Submit(info.GroupId ?? "", playerId, info.Payload ?? "");
        return new ValueTask<string>(httpResponseUtil.GetBody("ok"));
    }

    public ValueTask<string> HandleListSummaries(string url, RaidSummaryListRequest info, MongoId sessionId)
    {
        var payloads = RaidSummaryAggregator.List(info.GroupId ?? "", sessionId.ToString());

        var arrayJson = "[" + string.Join(",", payloads) + "]";
        return new ValueTask<string>(httpResponseUtil.GetBody(arrayJson));
    }
}
