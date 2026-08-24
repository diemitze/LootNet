using System;
using System.Collections.Generic;

namespace LootNet.Services
{
    /// One closing line under the total. Picked from the most specific bucket the raid matches.
    public static class Quips
    {
        private static readonly System.Random Rng = new();
        private static string _lastPicked;

        private static readonly string[] DiedRich =
        {
            "That is a very expensive corpse.",
            "Somewhere, a Scav just retired.",
            "You were the loot piñata today.",
            "Insurance does not cover emotional damage.",
            "Whoever killed you is now the best equipped bot on the map.",
            "All of that, delivered straight into someone else's pockets.",
            "Every last item, gifted to whoever found you.",
            "You held it for almost the whole raid.",
            "That backpack had a great, short life.",
            "It was all yours right up until it was not.",
            "Peak looting. Zero extraction.",
            "You were carrying a fortune and a target.",
        };

        private static readonly string[] DiedFighter =
        {
            "You took people with you. That counts for something.",
            "Went down swinging, at least.",
            "Not a quiet death, then.",
            "You made it expensive for them.",
            "Lost the raid, won the fight. Mostly.",
            "Bodies everywhere and still no extract.",
            "Aggression is a strategy. Just not a good one.",
            "You were the problem right up until you were not.",
            "Statistically you won. Practically you died.",
        };

        private static readonly string[] DiedEmpty =
        {
            "Not a single item. Impressive, in a way.",
            "You came, you saw, you died.",
            "Nothing gained, everything lost.",
            "A round trip with no cargo.",
            "Straight there, straight down.",
            "Not even a bolt to show for it.",
            "Speedrun complete.",
            "That was a very short story.",
            "You skipped the looting part entirely.",
        };

        private static readonly string[] DiedBroke =
        {
            "At least it was a cheap lesson.",
            "You died as you lived: broke.",
            "Barely worth the bullet.",
            "Nothing ventured, nothing lost.",
            "Cheapest raid you have lost all week.",
            "The kit cost more than the loot.",
            "Whoever looted you was disappointed.",
            "Small stakes, small tragedy.",
            "You lost almost nothing. Almost.",
        };

        private static readonly string[] DiedDefault =
        {
            "Tarkov giveth, Tarkov taketh away.",
            "Better luck next raid.",
            "Rest in pieces.",
            "The map remains undefeated.",
            "That one is going to sting for a bit.",
            "Caught somewhere between greed and the extract.",
            "It happens. Repeatedly.",
            "The next raid is already loading.",
            "You will be back in there in two minutes anyway.",
            "Write it off as reconnaissance.",
            "The insurance man will call. Eventually.",
        };

        private static readonly string[] SurvivedHuge =
        {
            "Retire? In this economy? Yes.",
            "The flea market is not ready for you.",
            "That is rent for the month.",
            "Absolute banger of a raid.",
            "Try not to lose it all next raid.",
            "Do not spend it all on ammo.",
            "A whole hideout upgrade in one bag.",
            "An unreasonable amount of loot for one man.",
            "Genuinely a problem for the local economy.",
            "You can afford to be reckless now. Do not be.",
            "Somewhere a Scav is crying about this.",
        };

        private static readonly string[] SurvivedGood =
        {
            "Solid haul. Nothing to be ashamed of.",
            "Therapist will see you now.",
            "An honest day's looting.",
            "Out clean and heavier than you went in.",
            "Nothing dramatic. Just profit.",
            "Professional work.",
            "In, took things, left. Correct.",
            "That will keep you kitted for a while.",
            "Quietly excellent.",
        };

        private static readonly string[] SurvivedEmpty =
        {
            "You extracted with your dignity and nothing else.",
            "A tactical stroll, then.",
            "Zero loot. Zero risk taken.",
            "You went for a walk and came home.",
            "Sightseeing, then.",
            "You visited. That was nice.",
            "Nothing on you, nothing lost. Balanced.",
            "The bravest option: touch nothing.",
            "A raid so clean it left no evidence.",
        };

        private static readonly string[] SurvivedBroke =
        {
            "You risked your life for pocket change.",
            "Barely worth the extraction.",
            "It is the exercise that counts.",
            "The bolts will be worth something one day.",
            "A rouble is a rouble.",
            "That will almost cover the ammo.",
            "Every empire starts somewhere. Probably not here.",
            "You made it out with vibes.",
            "Technically profitable. Technically.",
        };

        private static readonly string[] SurvivedFighter =
        {
            "Extracted covered in other people's problems.",
            "You did not come here to loot, clearly.",
            "Someone had to thin the population.",
            "Loot was clearly optional today.",
            "Out with mostly other people's kit.",
            "Kill count up, backpack empty. Priorities.",
            "The map is quieter now.",
            "You went hunting and forgot the shopping list.",
        };

        private static readonly string[] SurvivedDefault =
        {
            "Out clean. That is a win.",
            "Another one for the books.",
            "No notes. Just leave.",
            "Boring is underrated.",
            "No drama. Take the win.",
            "Uneventful. The best kind.",
            "In, out, no complaints.",
            "A raid happened. You survived it.",
            "Textbook. Boring. Perfect.",
        };

        public static string Pick(RaidStats s)
        {
            var pool = Choose(s);
            if (pool.Length == 0) return string.Empty;

            // avoid repeating the previous raid's line back to back
            for (int i = 0; i < 6; i++)
            {
                var line = pool[Rng.Next(pool.Length)];
                if (line != _lastPicked || pool.Length == 1) { _lastPicked = line; return line; }
            }
            return pool[0];
        }

        private static string[] Choose(RaidStats s)
        {
            int kills = s.PmcKills + s.ScavKills;

            if (!s.PlayerSurvived)
            {
                if (s.TotalFoundValue >= 800_000) return DiedRich;
                if (s.PmcKills >= 3)              return DiedFighter;
                if (s.ItemsFound == 0)            return DiedEmpty;
                if (s.TotalFoundValue < 50_000)   return DiedBroke;
                return DiedDefault;
            }

            if (s.TotalFoundValue >= 1_000_000) return SurvivedHuge;
            if (s.ItemsFound == 0)              return SurvivedEmpty;
            if (s.TotalFoundValue < 50_000)     return SurvivedBroke;
            if (kills >= 3)                     return SurvivedFighter;
            if (s.TotalFoundValue >= 300_000)   return SurvivedGood;
            return SurvivedDefault;
        }
    }
}
