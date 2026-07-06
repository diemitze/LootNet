using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.UI;
using HarmonyLib;
using LootNet.Patches;
using LootNet.Services;
using LootNet.UI;
using System;
using System.Reflection;
using UnityEngine;

namespace LootNet
{
    [BepInPlugin("com.20fpsguy.LootNet", "LootNet", "1.0.8")]
    [BepInDependency("com.20fpsguy.QuickLootServer", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource LogSource;
        public static PriceService PriceService;
        public static LootValueDisplay Display;
        public static RaidSummaryDisplay SummaryDisplay;
        public static ConfigEntry<bool> VideoEnabled;
        public static ConfigEntry<bool> UseHandbookPrices;
        public static ConfigEntry<bool> ManualDismiss;
        public static ConfigEntry<bool> ShowInRaidCounter;
        public static ConfigEntry<bool> DefaultFleaRules;
        public static ConfigEntry<bool> PersistRaidHistory;

        private static void PatchAllKillMethods()
        {
            var harmony  = new Harmony("com.20fpsguy.LootNet.kills");
            var postfix  = new HarmonyMethod(
                typeof(KillTracker).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static));
            var deathPostfix = new HarmonyMethod(
                typeof(DeathTracker).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static));

            int count = 0;
            int deathCount = 0;
            foreach (var type in typeof(Player).Assembly.GetTypes())
            {
                if (!typeof(IPlayer).IsAssignableFrom(type)) continue;

                var method = type.GetMethod(
                    nameof(Player.OnBeenKilledByAggressor),
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    try { harmony.Patch(method, postfix: postfix); count++; }
                    catch (Exception ex)
                    {
                        LogSource.LogWarning(
                            $"[LootNet] Could not patch {type.Name}.OnBeenKilledByAggressor: {ex.Message}");
                    }
                }

                var deathMethod = type.GetMethod(
                    nameof(Player.OnDead),
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (deathMethod != null)
                {
                    try { harmony.Patch(deathMethod, postfix: deathPostfix); deathCount++; }
                    catch (Exception ex)
                    {
                        LogSource.LogWarning(
                            $"[LootNet] Could not patch {type.Name}.OnDead: {ex.Message}");
                    }
                }
            }

            LogSource.LogInfo($"[LootNet] Kill tracking patched across {count} type(s), death tracking across {deathCount} type(s).");
        }

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            VideoEnabled = Config.Bind("Display", "Secret Summary Feature", false,
                "You found it. Enable this and see what happens after your next raid.");
            UseHandbookPrices = Config.Bind("Prices", "Use Handbook Prices", false,
                "Use handbook (base) prices instead of flea market prices. Enable this if you use custom traders or have no flea market. Requires a game restart to take effect.");
            ManualDismiss = Config.Bind("Display", "Manual Dismiss Only", false,
                "When enabled, the raid summary screen stays open until you click to close it the auto-dismiss timer is disabled.");
            ShowInRaidCounter = Config.Bind("Display", "Show In-Raid Inventory Counter", true,
                "When enabled, shows the running loot value counter inside your inventory screen during a raid. Disable to hide it.");
            DefaultFleaRules = Config.Bind("Prices", "Default Flea Market Rules", true,
                "When ON, only items that can be sold on the flea market are counted toward raid value (matches vanilla rules). When OFF, all items are tracked - including quest items and other normally-untradeable items. Turn OFF if you use a mod that unlocks the flea market filter.");
            PersistRaidHistory = Config.Bind("Display", "Persist Raid History", false,
                "When ON, your raid history is saved to disk (BepInEx/plugins/LootNet/raid_history.json) and reloaded on game start, so old raids and your best raid stay visible across sessions. Capped at 15 raids - oldest are dropped. Delete the file to wipe history.");

            if (PersistRaidHistory.Value) RaidTracker.LoadPersistedHistory();

            var priceObj = new GameObject("LootNetPriceService");
            DontDestroyOnLoad(priceObj);
            PriceService = priceObj.AddComponent<PriceService>();
            PriceService.FetchPrices();

            var trackerObj = new GameObject("LootNetRaidTracker");
            DontDestroyOnLoad(trackerObj);
            trackerObj.AddComponent<RaidTracker>();

            StartCoroutine(RaidTracker.PollLoop());

            Display = LootValueDisplay.Instance;
            SummaryDisplay = RaidSummaryDisplay.Instance;
            _ = RaidHistoryDisplay.Instance;
            _ = TeamSummaryDisplay.Instance;

            if (VideoEnabled.Value)
                SummaryDisplay.StartCoroutine(SummaryDisplay.PrepareVideoEarly());

            new RaidStartPatch().Enable();
            new InventoryPatch().Enable();
            new InventoryRemovePatch().Enable();
            new InventoryScreenShowPatch().Enable();
            new InventoryScreenClosePatch().Enable();
            new RaidEndPatch().Enable();
            new MenuScreenPatch().Enable();
            PatchAllKillMethods();

            LogSource.LogInfo("LootNet v1.0.8 loaded!");
        }
    }
}
