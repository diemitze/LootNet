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
    [BepInPlugin("com.20fpsguy.LootNet", "LootNet", "2.0.0")]
    [BepInDependency("com.20fpsguy.QuickLootServer", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static PriceService PriceService;
        public static LootValueDisplay Display;
        public static RaidSummaryDisplay SummaryDisplay;
        public static ConfigEntry<bool> VideoEnabled;
        public static ConfigEntry<bool> UseHandbookPrices;

        private static void PatchAllKillMethods()
        {
            var harmony  = new Harmony("com.20fpsguy.LootNet.kills");
            var postfix  = new HarmonyMethod(
                typeof(KillTracker).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static));

            int count = 0;
            foreach (var type in typeof(Player).Assembly.GetTypes())
            {
                if (!typeof(IPlayer).IsAssignableFrom(type)) continue;

                var method = type.GetMethod(
                    nameof(Player.OnBeenKilledByAggressor),
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method == null) continue;

                try { harmony.Patch(method, postfix: postfix); count++; }
                catch (Exception ex)
                {
                    LogSource.LogWarning(
                        $"[LootNet] Could not patch {type.Name}.OnBeenKilledByAggressor: {ex.Message}");
                }
            }

            LogSource.LogInfo($"[LootNet] Kill tracking patched across {count} type(s).");
        }

        private void Awake()
        {
            LogSource = Logger;
            VideoEnabled = Config.Bind("Display", "Secret Summary Feature", false,
                "You found it. Enable this and see what happens after your next raid.");
            UseHandbookPrices = Config.Bind("Prices", "Use Handbook Prices", false,
                "Use handbook (base) prices instead of flea market prices. Enable this if you use custom traders or have no flea market. Requires a game restart to take effect.");

            var priceObj = new GameObject("LootNetPriceService");
            DontDestroyOnLoad(priceObj);
            PriceService = priceObj.AddComponent<PriceService>();
            PriceService.FetchPrices();

            var trackerObj = new GameObject("LootNetRaidTracker");
            DontDestroyOnLoad(trackerObj);
            trackerObj.AddComponent<RaidTracker>();

            Display = LootValueDisplay.Instance;
            SummaryDisplay = RaidSummaryDisplay.Instance;

            // buffer the video at menu load so there's no hitch mid-raid
            if (VideoEnabled.Value)
                SummaryDisplay.StartCoroutine(SummaryDisplay.PrepareVideoEarly());

            new RaidStartPatch().Enable();
            new InventoryPatch().Enable();
            new InventoryRemovePatch().Enable();
            new InventoryScreenShowPatch().Enable();
            new InventoryScreenClosePatch().Enable();
            new RaidEndPatch().Enable();
            PatchAllKillMethods();

            LogSource.LogInfo("LootNet v2.0.0 loaded!");
        }
    }
}
