using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using LootNet.UI;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Comfort.Common;

namespace LootNet.Services
{
    public class RaidStats
    {
        public double TotalFoundValue;
        public int ItemsFound;
        public int PmcKills;
        public int ScavKills;
        public List<(string Name, double Value)> TopItems = new();
        public List<(string Name, int Kills)> FireteamMembers;
        public string MapName;
        public DateTime RaidTime;
        public bool IsScavRaid;
    }

    public class RaidTracker : MonoBehaviour
    {
        public static RaidTracker Instance { get; private set; }

        public static HashSet<string> SpawnedItemIds { get; } = new();
        public static bool IsScavRaid { get; private set; }
        public static bool IsInRaid   { get; private set; }

        public static readonly List<RaidStats> RaidHistory = new();
        internal const int MaxHistory = 15;

        private static readonly Dictionary<string, (string Name, string TemplateId, double Value)> _foundItems = new();
        private static readonly Dictionary<string, (string Name, int Kills)> _botKills = new();
        private static readonly HashSet<string> _recentKillIds = new(); // base + override both fire; deduplicate by profileId
        private static readonly HashSet<string> _confirmedFollowerIds = new(); // captured at kill time - PIT unregisters followers when they die
        private static int _pmcKills;
        private static int _scavKills;
        private static string _pendingMapName;
        private static DateTime _pendingRaidTime;

        private static bool _pitResolved;
        private static MethodInfo _pitIsFollower;
        private static Func<string, bool> _isFollower;

        public static double DisplayValue
        {
            get
            {
                double sum = 0;
                foreach (var kv in _foundItems) sum += kv.Value.Value;
                return sum;
            }
        }

        private void Awake() => Instance = this;

        private static void ClearRaidState()
        {
            SpawnedItemIds.Clear();
            _foundItems.Clear();
            _botKills.Clear();
            _recentKillIds.Clear();
            _confirmedFollowerIds.Clear();
            _pmcKills  = 0;
            _scavKills = 0;
        }

        public static void OnRaidEntered(Player player)
        {
            if (player == null) return;

            ClearRaidState();
            IsScavRaid      = player.Side == EPlayerSide.Savage;
            IsInRaid        = true;
            _pendingMapName = TryGetMapName();
            _pendingRaidTime = DateTime.Now;
            ResolvePit(); // needs to be ready before the first kill fires

            var spawnSlots = EPlayerItems.InRaidItems;

            if (IsScavRaid)
            {
                // scav gear counts from spawn, not just stuff picked up during the raid
                foreach (var item in player.Inventory.GetPlayerItems(spawnSlots))
                    foreach (var child in item.GetAllItems())
                        TrackItemAdded(child);
            }
            else
            {
                foreach (var item in player.Inventory.GetPlayerItems(spawnSlots))
                    foreach (var child in item.GetAllItems())
                        SpawnedItemIds.Add(child.Id.ToString());
            }
        }

        public static void TrackItemAdded(Item item)
        {
            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            if (_foundItems.ContainsKey(id)) return;

            string templateId = item.TemplateId.ToString();
            double price = Plugin.PriceService.IsLoaded ? Plugin.PriceService.GetPrice(templateId) : 0;
            _foundItems[id] = (item.LocalizedName(), templateId, price);
        }

        public static void RefreshPrices()
        {
            var keys = _foundItems.Keys.ToList();
            foreach (var key in keys)
            {
                var (name, templateId, _) = _foundItems[key];
                _foundItems[key] = (name, templateId, Plugin.PriceService.GetPrice(templateId));
            }
        }

        public static void TrackItemRemoved(Item item)
        {
            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            _foundItems.Remove(id);
        }

        public static bool MarkKillSeen(string profileId) => _recentKillIds.Add(profileId);

        public static void TrackKill(bool isPmc)
        {
            if (isPmc) _pmcKills++;
            else       _scavKills++;
        }

        public static void TrackBotKill(IPlayer bot)
        {
            string id   = bot.ProfileId;
            string name = bot.Profile?.Nickname ?? (bot as UnityEngine.Object)?.name ?? "Follower";
            _botKills.TryGetValue(id, out var existing);
            _botKills[id] = (name, existing.Kills + 1);

            // check PIT now while the follower is still registered; dead followers get unregistered before raid-end
            if (_isFollower != null && !_confirmedFollowerIds.Contains(id))
            {
                try { if (_isFollower(id)) _confirmedFollowerIds.Add(id); }
                catch { }
            }
        }

        public static RaidStats BuildPendingStats()
        {
            bool hasLoot  = _foundItems.Count > 0;
            bool hasKills = _pmcKills > 0 || _scavKills > 0;
            if (!hasLoot && !hasKills) return null;

            var allFound = _foundItems.Values.Where(x => x.Value > 0).ToList();

            var grouped = allFound
                .GroupBy(x => x.TemplateId)
                .Select(g =>
                {
                    int    count = g.Count();
                    double total = g.Sum(x => x.Value);
                    string name  = count > 1 ? $"{g.First().Name} x{count}" : g.First().Name;
                    return (name, total);
                })
                .OrderByDescending(x => x.total)
                .ToList();

            var stats = new RaidStats
            {
                ItemsFound      = allFound.Count,
                TotalFoundValue = allFound.Sum(x => x.Value),
                PmcKills        = _pmcKills,
                ScavKills       = _scavKills,
                TopItems        = grouped.Take(5).ToList(),
                FireteamMembers = TryGetFireteamStats(),
                MapName         = _pendingMapName ?? "Unknown",
                RaidTime        = _pendingRaidTime,
                IsScavRaid      = IsScavRaid,
            };

            if (RaidHistory.Count >= MaxHistory) RaidHistory.RemoveAt(0);
            RaidHistory.Add(stats);

            return stats;
        }

        public static void ResetAfterRaid()
        {
            ClearRaidState();
            IsScavRaid = false;
            IsInRaid   = false;
        }

        private static List<(string Name, int Kills)> TryGetFireteamStats()
        {
            if (_isFollower == null) return null;
            if (_confirmedFollowerIds.Count == 0) return null;

            var result = new List<(string Name, int Kills)>();
            foreach (var id in _confirmedFollowerIds)
            {
                if (_botKills.TryGetValue(id, out var entry))
                    result.Add((entry.Name, entry.Kills));
            }

            result.Sort((a, b) => b.Kills.CompareTo(a.Kills));
            return result.Count > 0 ? result : null;
        }

        private static string TryGetMapName()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                if (gw == null) return "Unknown";

                // Try common property/field names for the location ID
                foreach (var name in new[] { "LocationId", "Location_0", "_locationId", "location" })
                {
                    var prop = typeof(GameWorld).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop?.GetValue(gw) is string ps && !string.IsNullOrEmpty(ps))
                        return FormatMapName(ps);

                    var field = typeof(GameWorld).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field?.GetValue(gw) is string fs && !string.IsNullOrEmpty(fs))
                        return FormatMapName(fs);
                }

                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        private static readonly Dictionary<string, string> _mapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bigmap"]        = "Customs",
            ["interchange"]   = "Interchange",
            ["woods"]         = "Woods",
            ["shoreline"]     = "Shoreline",
            ["rezervbase"]    = "Reserve",
            ["lighthouse"]    = "Lighthouse",
            ["tarkovstreets"] = "Streets",
            ["sandbox"]       = "Ground Zero",
            ["sandbox_high"]  = "Ground Zero (High)",
            ["laboratory"]    = "Labs",
            ["factory4_day"]  = "Factory (Day)",
            ["factory4_night"]= "Factory (Night)",
        };

        private static string FormatMapName(string raw)
        {
            if (_mapNames.TryGetValue(raw, out var friendly)) return friendly;
            // Titlecase the raw string as fallback
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.ToLower());
        }

        private static void ResolvePit()
        {
            if (_pitResolved) return;
            _pitResolved = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type bossPlayers = null;
                try { bossPlayers = Array.Find(asm.GetTypes(), t => t.Name == "BossPlayers"); }
                catch { continue; }
                if (bossPlayers == null) continue;

                var isFollower = bossPlayers.GetMethod("IsFollowerProfileId", BindingFlags.Public | BindingFlags.Static);
                if (isFollower == null) continue;

                _pitIsFollower = isFollower;
                _isFollower    = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), isFollower);
                Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam detected - fireteam stats enabled.");
                return;
            }

            Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam not found - fireteam section disabled.");
        }
    }

    internal class RaidStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(GameWorld), "OnGameStarted");

        [PatchPostfix]
        private static void PatchPostfix()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer as Player;
            RaidTracker.OnRaidEntered(player);

        }
    }

    // patched via raw Harmony in Plugin.cs so every Player subclass override is caught
    internal static class KillTracker
    {
        // __0 is the second param by position - the name varies across EFT Player subclasses
        internal static void Postfix(Player __instance, IPlayer __0, DamageInfoStruct __1)
        {
            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
                if (mainPlayer == null) return;
                if (__instance.ProfileId == mainPlayer.ProfileId) return;

                IPlayer aggressor = __0 ?? __1.Player?.iPlayer;
                if (aggressor == null) return;

                if (!RaidTracker.MarkKillSeen(__instance.ProfileId)) return;

                bool isPmc = __instance.Side == EPlayerSide.Bear
                          || __instance.Side == EPlayerSide.Usec;

                bool isPlayerKill = aggressor.ProfileId == mainPlayer.ProfileId
                                 || (aggressor is Player p && p.IsYourPlayer);

                if (isPlayerKill)
                    RaidTracker.TrackKill(isPmc);
                else
                    RaidTracker.TrackBotKill(aggressor);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LN Kill] Exception: {ex.Message}");
            }
        }
    }
}
