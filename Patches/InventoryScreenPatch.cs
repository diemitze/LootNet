using EFT.UI.SessionEnd;
using HarmonyLib;
using LootNet.Services;
using LootNet.UI;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace LootNet.Patches
{
    internal class RaidEndPatch : ModulePatch
    {
        private static bool _fired;

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(SessionResultExitStatus), nameof(SessionResultExitStatus.Awake));

        [PatchPostfix]
        private static void PatchPostfix(SessionResultExitStatus __instance)
        {
            if (_fired) return;
            _fired = true;

            RaidTracker.OpenExtraPollWindow(4f);
            var stats = RaidTracker.BuildPendingStats();
            RaidTracker.ResetAfterRaid();
            LootValueDisplay.Instance.DestroyClone();

            if (stats == null) { _fired = false; return; }

            var endCg = __instance.gameObject.GetComponent<CanvasGroup>()
                     ?? __instance.gameObject.AddComponent<CanvasGroup>();
            endCg.alpha = 0f;

            bool teamRaid = RaidTracker.IsTeamRaid?.Invoke() == true;
            if (teamRaid)
            {
                TeamSummaryDisplay.Instance.ShowForRaid(stats);
                TeamSummaryDisplay.Instance.OnClosed += () =>
                {
                    _fired = false;
                    if (endCg != null) endCg.alpha = 1f;
                };
            }
            else
            {
                RaidSummaryDisplay.Instance.QueueSummary(stats);
                RaidSummaryDisplay.Instance.OnHidden += () =>
                {
                    _fired = false;
                    if (endCg != null) endCg.alpha = 1f;
                };
            }
        }
    }
}
