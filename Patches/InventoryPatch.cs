using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using LootNet.Services;
using LootNet.UI;
using SPT.Reflection.Patching;
using System.Reflection;
using EFT.InventoryLogic;

namespace LootNet.Patches
{
    internal class InventoryPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            // 4.1 made these explicit interface implementations, so they need the qualified name
            => AccessTools.Method(typeof(Player), "EFT.IAddHandler.OnItemAdded");

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, ItemEventArgs eventArgs)
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
            => AccessTools.Method(typeof(Player), "EFT.IRemoveHandler.OnItemRemoved");

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, RemoveItemEventArgs eventArgs)
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
            if (!Plugin.ShowInRaidCounter.Value) return;
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
