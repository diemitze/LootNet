using EFT.UI;
using HarmonyLib;
using LootNet.UI;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace LootNet.Patches
{
    internal class MenuScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MenuScreen), "Awake");

        [PatchPostfix]
        private static void PatchPostfix()
        {

            RaidHistoryDisplay.Instance.StartCoroutine(InjectNextFrame());
        }

        private static IEnumerator InjectNextFrame()
        {
            yield return null;
            try
            {
                RaidHistoryMenuButton.TryInject();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LootNet] Button inject failed: {ex.Message}");
            }
        }
    }
}
