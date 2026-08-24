using System;
using Comfort.Common;
using EFT.UI;

namespace LootNet.Services
{
    /// Thin wrapper over the game's interface sounds. Names are resolved by string so a
    /// renamed enum member degrades to the fallback instead of throwing.
    public static class LootSounds
    {
        // cinema summary cues
        public const string EnterSurvived   = "BackpackOpen";
        public const string EnterDied       = "PlayerIsDead";
        public const string Bars            = "MenuOpenContainer";
        public const string Tick            = "ButtonClick";
        public const string Total           = "QuestSubTrackComplete";
        public const string ResolveSurvived = "AchievementCompleted";
        public const string ResolveDied     = "QuestFailed";
        public const string Close           = "MenuEscape";

        public static void Play(string soundName, string fallback = null)
        {
            if (string.IsNullOrEmpty(soundName)) return;
            try
            {
                var gs = Singleton<GUISounds>.Instance;
                if (gs == null) return;
                if (Enum.TryParse(soundName, out EUISoundType s)) { gs.PlayUISound(s); return; }
                if (fallback != null && Enum.TryParse(fallback, out EUISoundType f)) gs.PlayUISound(f);
            }
            catch { }
        }
    }
}
