using System;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Fika.Core.Main.Components;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using HarmonyLib;
using LootNet.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace LootNet.Fika
{
    // Thin loader. This type is Fika-free on purpose: Mono resolves the types used in
    // a class's *method signatures* at class-load time (unlike .NET Framework), so any
    // Fika type here would make BepInEx's AddComponent throw a FileNotFoundException
    // before Awake can run — even with a soft dependency. All Fika-touching code lives
    // in FikaBridge, which is only loaded once we've confirmed Fika is installed.
    [BepInPlugin("com.20fpsguy.LootNet.fika", "LootNet.Fika", "1.0.7")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.20fpsguy.LootNet")]
    internal class LootNetFikaPlugin : BaseUnityPlugin
    {
        private const string FikaGuid = "com.fika.core";

        internal static ManualLogSource Log;

        protected void Awake()
        {
            Log = Logger;

            if (!Chainloader.PluginInfos.ContainsKey(FikaGuid))
            {
                Log.LogInfo("Fika not installed");
                return;
            }

            // Fika is present: loading FikaBridge resolves Fika.Core, which is safe now.
            gameObject.AddComponent<FikaBridge>();
        }
    }

    // Carries every Fika type reference. Only instantiated when Fika is confirmed
    // present, so Mono never has to resolve Fika.Core when it isn't installed.
    internal class FikaBridge : MonoBehaviour
    {
        private static ManualLogSource Log => LootNetFikaPlugin.Log;

        private static string _groupId;
        private static string _playerId;
        private static string _nickname = "Teammate";

        private static bool _isTeamRaid;

        private static int _expectedTeammates;

        private static bool _snapshotSubmitted;

        private float _captureTimer;

        private const string SubmitPath = "/lootnet/raidsummary/submit";
        private const string ListPath   = "/lootnet/raidsummary/list";

        protected void Awake()
        {
            RaidTracker.OnLocalSummaryBuilt     += OnLocalSummaryBuilt;
            TeamSummaryStore.OnRefreshRequested += OnRefreshRequested;

            RaidTracker.IsTeamRaid               = () => _isTeamRaid && !FikaBackendUtils.IsHeadless;
            RaidTracker.ExpectedTeammates        = () => _expectedTeammates;

            try
            {
                var harmony = new Harmony("com.20fpsguy.LootNet.fika");
                harmony.Patch(AccessTools.Method(typeof(CoopGame), "Stop"),
                    postfix: new HarmonyMethod(typeof(FikaBridge), nameof(OnCoopGameStop)));
                harmony.Patch(AccessTools.Method(typeof(CoopGame), "Extract"),
                    postfix: new HarmonyMethod(typeof(FikaBridge), nameof(OnCoopGameExtract)));
            }
            catch (Exception ex) { Log.LogWarning($"Failed to hook CoopGame for extract relay: {ex.Message}"); }

            Log.LogInfo("LootNet.Fika loaded — teammate loot summaries via SPT server relay.");
        }

        protected void Update()
        {
            _captureTimer -= Time.unscaledDeltaTime;
            if (_captureTimer > 0f) return;
            _captureTimer = 2f;
            TryCaptureRaidIdentity();
        }

        private static void TryCaptureRaidIdentity()
        {
            try
            {
                if (!CoopHandler.TryGetCoopHandler(out var ch) || ch == null) return;
                string sid = ch.ServerId;
                if (string.IsNullOrEmpty(sid)) return;

                if (sid != _groupId)
                {
                    _groupId = sid;
                    _isTeamRaid = false;
                    _expectedTeammates = 0;
                    _snapshotSubmitted = false;
                    if (ch.MyPlayer != null) _playerId = ch.MyPlayer.ProfileId;
                    if (string.IsNullOrEmpty(_playerId)) _playerId = RequestHandler.SessionId;
                    Log.LogDebug($"[LootNet.Fika] Captured Fika raid group '{_groupId}' (me: {_playerId}).");
                }

                int others = Mathf.Max(0, ch.AmountOfHumans - 1);
                if (others > _expectedTeammates) _expectedTeammates = others;

                if (!_isTeamRaid && ch.AmountOfHumans > 1)
                {
                    _isTeamRaid = true;
                    Log.LogDebug($"[LootNet.Fika] Coop raid detected ({ch.AmountOfHumans} humans).");
                }

                if ((_nickname == "Teammate" || string.IsNullOrEmpty(_nickname)) && ch.MyPlayer != null)
                {
                    string nn = ch.MyPlayer.Profile?.Nickname;
                    if (!string.IsNullOrEmpty(nn)) _nickname = nn;
                }
            }
            catch {  }
        }

        private static void OnCoopGameStop() => RelayLocalSnapshot();

        private static void OnCoopGameExtract(FikaPlayer __0)
        {
            try
            {
                if (__0 != null && __0.IsYourPlayer) RelayLocalSnapshot();
            }
            catch (Exception ex) { Log?.LogWarning($"Extract hook failed: {ex.Message}"); }
        }

        private static void RelayLocalSnapshot()
        {
            try
            {
                if (FikaBackendUtils.IsHeadless || _snapshotSubmitted) return;

                TryCaptureRaidIdentity();
                if (!_isTeamRaid || string.IsNullOrEmpty(_groupId)) return;
                if (string.IsNullOrEmpty(_playerId)) _playerId = RequestHandler.SessionId;

                var stats = RaidTracker.ComputeStats();
                if (stats == null) return;

                _snapshotSubmitted = true;
                var dto = TeamSummaryDto.From(stats, _playerId, _nickname);
                Log.LogDebug($"[LootNet.Fika] Extract snapshot relayed (group {_groupId}, {_nickname}, ₽{stats.TotalFoundValue:N0}).");
                _ = SubmitAsync(_groupId, _playerId, dto);
            }
            catch (Exception ex) { Log?.LogWarning($"Extract snapshot relay failed: {ex.Message}"); }
        }

        private void OnLocalSummaryBuilt(RaidStats stats)
        {
            try
            {

                if (FikaBackendUtils.IsHeadless)
                {
                    Log.LogDebug("[LootNet.Fika] Headless host — skipping summary relay.");
                    return;
                }

                TryCaptureRaidIdentity();
                if (string.IsNullOrEmpty(_playerId)) _playerId = RequestHandler.SessionId;

                if (!_isTeamRaid)
                {
                    Log.LogDebug("[LootNet.Fika] Solo raid — summary not relayed.");
                    return;
                }

                if (string.IsNullOrEmpty(_groupId))
                {
                    Log.LogWarning("[LootNet.Fika] No Fika group id captured this raid — summary not relayed.");
                    return;
                }

                var dto = TeamSummaryDto.From(stats, _playerId, _nickname);
                Log.LogDebug($"[LootNet.Fika] Submitting summary (group {_groupId}, {_nickname}, ₽{stats.TotalFoundValue:N0}).");
                _ = SubmitAsync(_groupId, _playerId, dto);
            }
            catch (Exception ex) { Log.LogError($"Summary submit prep failed: {ex}"); }
        }

        private void OnRefreshRequested()
        {
            if (string.IsNullOrEmpty(_groupId))
            {
                Log.LogWarning("[LootNet.Fika] Refresh requested but no group id captured.");
                return;
            }
            _ = FetchAsync(_groupId);
        }

        private static async Task SubmitAsync(string groupId, string playerId, TeamSummaryDto dto)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new SubmitRequest
                {
                    GroupId  = groupId,
                    PlayerId = playerId,
                    Payload  = JsonConvert.SerializeObject(dto),
                });
                await RequestHandler.PostJsonAsync(SubmitPath, body);
            }
            catch (Exception ex) { Log?.LogWarning($"Summary submit failed: {ex.Message}"); }
        }

        private static async Task FetchAsync(string groupId)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new ListRequest { GroupId = groupId });
                string resp = await RequestHandler.PostJsonAsync(ListPath, body);
                if (string.IsNullOrEmpty(resp)) return;

                JToken data = JObject.Parse(resp)["data"];
                if (data == null) return;

                JArray arr = data.Type == JTokenType.String
                    ? JArray.Parse(data.Value<string>() ?? "[]")
                    : data as JArray;
                if (arr == null) return;

                int added = 0;
                foreach (JToken item in arr)
                {
                    TeamSummaryDto dto;
                    try { dto = item.ToObject<TeamSummaryDto>(); }
                    catch { continue; }
                    if (dto == null) continue;
                    if (!string.IsNullOrEmpty(_playerId) && dto.PlayerId == _playerId) continue;

                    string key = string.IsNullOrEmpty(dto.PlayerId) ? dto.PlayerName : dto.PlayerId;
                    TeamSummaryStore.Submit(key, dto.ToStats());
                    added++;
                }
            }
            catch (Exception ex) { Log?.LogWarning($"Summary fetch failed: {ex.Message}"); }
        }

        private class SubmitRequest
        {
            [JsonProperty("groupId")]  public string GroupId  { get; set; }
            [JsonProperty("playerId")] public string PlayerId { get; set; }
            [JsonProperty("payload")]  public string Payload  { get; set; }
        }

        private class ListRequest
        {
            [JsonProperty("groupId")] public string GroupId { get; set; }
        }
    }
}
