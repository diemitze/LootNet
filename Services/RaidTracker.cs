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
    }

    public class RaidTracker : MonoBehaviour
    {
        public static RaidTracker Instance { get; private set; }

        public static HashSet<string> SpawnedItemIds { get; } = new();
        public static bool IsScavRaid { get; private set; }

        private static readonly Dictionary<string, (string Name, double Value)> _foundItems = new();
        private static readonly Dictionary<string, (string Name, int Kills)> _botKills = new();
        private static readonly HashSet<string> _recentKillIds = new(); // dedup base+override double-fires
        private static readonly HashSet<string> _confirmedFollowerIds = new(); // checked at kill time while PIT still has them registered
        private static int _pmcKills;
        private static int _scavKills;

        // PIT Fireteam soft-dependency — resolved once via reflection
        private static bool _pitResolved;
        private static MethodInfo _pitIsFollower;

        // Computed live from _foundItems so the in-raid display and end-of-raid
        // summary are always derived from exactly the same data — no running-total drift.
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

        // ── Raid start ────────────────────────────────────────────────────────────

        public static void OnRaidEntered(Player player)
        {
            if (player == null) return;

            SpawnedItemIds.Clear();
            _foundItems.Clear();
            _botKills.Clear();
            _recentKillIds.Clear();
            _confirmedFollowerIds.Clear();
            _pmcKills  = 0;
            _scavKills = 0;
            IsScavRaid = player.Side == EPlayerSide.Savage;
            ResolvePit(); // resolve early so IsFollowerProfileId is ready when kills fire

            if (IsScavRaid)
            {
                // Pre-track all scav spawn gear so it counts without needing to be moved
                foreach (var item in player.Inventory.GetPlayerItems(EPlayerItems.Equipment))
                    foreach (var child in item.GetAllItems())
                        TrackItemAdded(child);
            }
            else
            {
                foreach (var item in player.Inventory.GetPlayerItems(EPlayerItems.Equipment))
                    foreach (var child in item.GetAllItems())
                        SpawnedItemIds.Add(child.Id.ToString());
            }
        }

        // ── Item tracking (called from patches) ───────────────────────────────────

        public static void TrackItemAdded(Item item)
        {
            if (!Plugin.PriceService.IsLoaded) return;

            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            if (_foundItems.ContainsKey(id)) return;

            double price = Plugin.PriceService.GetPrice(item.TemplateId.ToString());
            _foundItems[id] = (item.LocalizedName(), price);
        }

        public static void TrackItemRemoved(Item item)
        {
            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            _foundItems.Remove(id);
        }

        /// <summary>Returns true the first time this profileId is seen this raid (dedup guard).</summary>
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

            // Check PIT registration NOW — followers get unregistered when they die,
            // so by raid-end IsFollowerProfileId would return false for dead followers.
            if (_pitIsFollower != null && !_confirmedFollowerIds.Contains(id))
            {
                try
                {
                    if ((bool)_pitIsFollower.Invoke(null, new object[] { id }))
                        _confirmedFollowerIds.Add(id);
                }
                catch { }
            }
        }

        // ── Raid end ──────────────────────────────────────────────────────────────

        public static RaidStats BuildPendingStats()
        {
            bool hasLoot  = _foundItems.Count > 0;
            bool hasKills = _pmcKills > 0 || _scavKills > 0;
            if (!hasLoot && !hasKills) return null;

            var found = _foundItems.Values.Where(x => x.Value > 0).ToList();
            found.Sort((a, b) => b.Value.CompareTo(a.Value));

            return new RaidStats
            {
                ItemsFound      = found.Count,
                TotalFoundValue = found.Sum(x => x.Value),
                PmcKills        = _pmcKills,
                ScavKills       = _scavKills,
                TopItems        = found.Take(5).ToList(),
                FireteamMembers = TryGetFireteamStats()
            };
        }

        public static void ResetAfterRaid()
        {
            SpawnedItemIds.Clear();
            _foundItems.Clear();
            _botKills.Clear();
            _recentKillIds.Clear();
            _confirmedFollowerIds.Clear();
            _pmcKills     = 0;
            _scavKills    = 0;
            IsScavRaid    = false;
        }

        // ── PIT Fireteam soft-dependency ──────────────────────────────────────────

        private static List<(string Name, int Kills)> TryGetFireteamStats()
        {
            if (_pitIsFollower == null) return null;
            if (_confirmedFollowerIds.Count == 0) return null;

            var result = new List<(string Name, int Kills)>();
            foreach (var id in _confirmedFollowerIds)
            {
                if (_botKills.TryGetValue(id, out var entry))
                    result.Add((entry.Name, entry.Kills));
            }

            result.Sort((a, b) => b.Kills.CompareTo(a.Kills));
            Plugin.LogSource.LogInfo($"[LootNet] PIT fireteam: {result.Count} follower kill(s) confirmed.");
            return result.Count > 0 ? result : null;
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
                Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam detected — fireteam stats enabled.");
                return;
            }

            Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam not found — fireteam section disabled.");
        }
    }

    // ── Raid-start patch ──────────────────────────────────────────────────────────

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

    // ── Kill tracking ─────────────────────────────────────────────────────────────
    // Applied via raw Harmony in Plugin.cs so every subclass override is caught,
    // not just the base Player implementation.

    internal static class KillTracker
    {
        // Second parameter by position — name varies across EFT subclasses
        internal static void Postfix(Player __instance, IPlayer __0, DamageInfoStruct __1)
        {
            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
                if (mainPlayer == null) return;
                if (__instance.ProfileId == mainPlayer.ProfileId) return; // ignore self-kill

                // Resolve aggressor: use method param first, fall back to damageInfo.Player.iPlayer
                IPlayer aggressor = __0 ?? __1.Player?.iPlayer;
                if (aggressor == null) return;

                // Deduplicate: base+override both patched — guard with per-raid set
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
