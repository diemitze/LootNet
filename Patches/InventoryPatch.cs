using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using LootNet.Services;
using LootNet.UI;
using SPT.Reflection.Patching;
using System.Reflection;

namespace LootNet.Patches
{
    internal class InventoryPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.OnItemAdded));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, GEventArgs1 eventArgs)
        {
            if (!__instance.IsYourPlayer) return;
            if (!RaidTracker.IsInRaid) return;

            foreach (var item in eventArgs.Item.GetAllItems())
                RaidTracker.TrackItemAdded(item);

            LootValueDisplay.Instance.SetValue(RaidTracker.DisplayValue);
        }
    }

    internal class InventoryRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.OnItemRemoved));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, GEventArgs3 eventArgs)
        {
            if (!__instance.IsYourPlayer) return;
            if (!RaidTracker.IsInRaid) return;

            foreach (var item in eventArgs.Item.GetAllItems())
                RaidTracker.TrackItemRemoved(item);

            LootValueDisplay.Instance.SetValue(RaidTracker.DisplayValue);
        }
    }

    internal class InventoryScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ContainersPanel), nameof(ContainersPanel.Show));

        [PatchPostfix]
        private static void PatchPostfix()
        {
            if (!RaidTracker.IsInRaid) return;
            LootValueDisplay.Instance.SetValue(RaidTracker.DisplayValue);
            LootValueDisplay.Instance.Show();
        }
    }

    internal class InventoryScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryScreen), nameof(InventoryScreen.Close));

        [PatchPostfix]
        private static void PatchPostfix()
            => LootValueDisplay.Instance?.Hide();
    }
}
