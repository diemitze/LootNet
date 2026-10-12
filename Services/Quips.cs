using System;
using System.Collections.Generic;

namespace LootNet.Services
{
    /// One closing line under the total. Picked from the most specific bucket the raid matches.
    public static class Quips
    {
        private static readonly System.Random Rng = new();
        private static readonly Queue<string> _recent = new();
        private const int RecentMemory = 12;

        // ---------- died ----------

        private static readonly string[] DiedRich =
        {
            "That is a very expensive corpse.",
            "Somewhere, a Scav just retired.",
            "You were the loot piñata today.",
            "Insurance does not cover emotional damage.",
            "Whoever killed you is now the best equipped bot on the map.",
            "All of that, delivered straight into someone else's pockets.",
            "Every last item, gifted to whoever found you.",
            "That backpack had a great, short life.",
            "It was all yours right up until it was not.",
            "Peak looting. Zero extraction.",
            "You were carrying a fortune and a target.",
            "Greed is a valid strategy. It just failed.",
            "The extract was right there. Probably.",
            "Someone is going to open that bag and scream.",
            "A fully stocked care package for the next player.",
            "You did the hard part. Someone else gets the payout.",
            "That was a down payment on a hideout upgrade. Was.",
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
            "The killfeed will remember you fondly.",
            "One more reload and that was a different story.",
            "You cleared the map. The map cleared you back.",
            "Chad behaviour, rat ending.",
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
            "The loading screen lasted longer than you did.",
            "Hatchet runner energy, minus the running.",
            "In and out. Mostly out of breath.",
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
            "Your killer is now rich in screws.",
            "A tragic loss of toothpaste.",
        };

        private static readonly string[] DiedScav =
        {
            "Free kit, free death. Balanced.",
            "Scav life comes with no refunds and no regrets.",
            "Fence will send another one.",
            "It was not your gear anyway.",
            "The cooldown starts now. Go do a PMC run.",
            "Some Scav lost a cousin today. It was you.",
            "No insurance, no worries.",
            "You were a borrowed body with a borrowed gun.",
        };

        private static readonly string[] DiedSquadWiped =
        {
            "Whole squad down. Nobody is writing home about this one.",
            "Team wipe. The bots will be talking about this.",
            "Nobody made it out. Not even the guy at the back.",
            "The fireteam has been dissolved. Permanently.",
            "Good news, nobody has to share the loot. Bad news, there is no loot.",
            "Squad goals: not met.",
            "Everyone went down together. That is loyalty, of a kind.",
            "You led them in. Nobody led them out.",
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
            "Every death is a lesson. This one was about corners.",
            "Somebody heard your footsteps.",
            "Head, eyes. Probably.",
        };

        // ---------- survived ----------

        private static readonly string[] SurvivedRecord =
        {
            "New personal best. Frame this one.",
            "Your best raid yet. Do not get cocky.",
            "That just became the raid you tell people about.",
            "Record haul. The bar is now uncomfortably high.",
            "Best one so far. The next one will be worse, statistically.",
            "Top of the list. Screenshot it before you lose it all.",
            "You will be chasing this raid for weeks.",
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
            "An unreasonable amount of loot for one person.",
            "Genuinely a problem for the local economy.",
            "You can afford to be reckless now. Do not be.",
            "Somewhere a Scav is crying about this.",
            "Therapist will see you now. You can afford it.",
            "Labs keycard money, easily.",
            "You walked out with someone's retirement fund.",
        };

        private static readonly string[] SurvivedGood =
        {
            "Solid haul. Nothing to be ashamed of.",
            "An honest day's looting.",
            "Out clean and heavier than you went in.",
            "Nothing dramatic. Just profit.",
            "Professional work.",
            "In, took things, left. Correct.",
            "That will keep you kitted for a while.",
            "Quietly excellent.",
            "The kind of raid that pays for the bad ones.",
            "Steady hands, full bags.",
            "That is how it is supposed to go.",
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
            "Cardio day.",
            "You ran a lap and called it a raid.",
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
            "Ah yes, the famous screw nut economy.",
            "Small bag, big heart.",
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
            "Several people will be rethinking their loadouts.",
            "You were the boss of that map today.",
            "Left a trail. Walked out anyway.",
        };

        private static readonly string[] SurvivedScav =
        {
            "Free kit, real profit. Fence approves.",
            "Not bad for a borrowed gun.",
            "Scav run paid off. Take it to the flea.",
            "Zero risk, some reward. Correct.",
            "Fence will pretend to be impressed.",
            "You were never here. The loot was, though.",
            "Scav karma intact. Probably.",
        };

        private static readonly string[] SurvivedSquadCarried =
        {
            "Your squad did the heavy lifting. You did the lifting of loot.",
            "The fireteam outshot you. Say thank you.",
            "Good leadership is knowing who to stand behind.",
            "The bots carried. You supervised.",
            "Your followers earned their wages today.",
            "Somebody in that squad deserves a raise. Not you.",
        };

        private static readonly string[] SurvivedSquadLost =
        {
            "You made it. Not everyone did.",
            "Out alive, one short. Raise a glass.",
            "Extracted with fewer friends than you brought.",
            "Losses were taken. The loot came home anyway.",
            "Somebody covered your exit. Remember the name.",
            "The squad paid for this one. You collected.",
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
            "Alive and slightly richer.",
            "Nobody shot you. Celebrate that.",
        };

        public static string Pick(RaidStats s)
        {
            var pool = Choose(s);
            if (pool.Length == 0) return string.Empty;

            // Skip anything shown in the last few raids, as long as the pool has something fresh.
            var fresh = new List<string>();
            foreach (var line in pool) if (!_recent.Contains(line)) fresh.Add(line);
            var from = fresh.Count > 0 ? fresh : new List<string>(pool);

            var pick = from[Rng.Next(from.Count)];
            _recent.Enqueue(pick);
            while (_recent.Count > RecentMemory) _recent.Dequeue();
            return pick;
        }

        private static string[] Choose(RaidStats s)
        {
            int kills = s.PmcKills + s.ScavKills;
            CountSquad(s, out int mates, out int matesDead, out int squadKills);

            if (!s.PlayerSurvived)
            {
                if (mates > 0 && matesDead == mates)  return DiedSquadWiped;
                if (s.TotalFoundValue >= 800_000)     return DiedRich;
                if (s.PmcKills >= 3)                  return DiedFighter;
                if (s.ItemsFound == 0)                return DiedEmpty;
                if (s.IsScavRaid)                     return DiedScav;
                if (s.TotalFoundValue < 50_000)       return DiedBroke;
                return DiedDefault;
            }

            if (IsRecord(s))                         return SurvivedRecord;
            if (s.TotalFoundValue >= 1_000_000)      return SurvivedHuge;
            if (matesDead > 0)                       return SurvivedSquadLost;
            if (squadKills >= 3 && squadKills > kills) return SurvivedSquadCarried;
            if (s.ItemsFound == 0)                   return SurvivedEmpty;
            if (kills >= 3)                          return SurvivedFighter;
            if (s.IsScavRaid)                        return SurvivedScav;
            if (s.TotalFoundValue < 50_000)          return SurvivedBroke;
            if (s.TotalFoundValue >= 300_000)        return SurvivedGood;
            return SurvivedDefault;
        }

        private static void CountSquad(RaidStats s, out int mates, out int dead, out int kills)
        {
            mates = dead = kills = 0;
            if (s.Fireteam != null)
            {
                foreach (var m in s.Fireteam)
                {
                    mates++;
                    if (!m.Alive) dead++;
                    kills += m.Kills;
                }
            }
            else if (s.FireteamMembers != null)
            {
                foreach (var (_, k) in s.FireteamMembers) { mates++; kills += k; }
            }
        }

        /// Best haul in stored history, once there is enough history for that to mean something.
        private static bool IsRecord(RaidStats s)
        {
            if (s.TotalFoundValue < 300_000) return false;
            int prior = 0;
            foreach (var h in RaidTracker.RaidHistory)
            {
                if (ReferenceEquals(h, s)) continue;
                if (h.TotalFoundValue >= s.TotalFoundValue) return false;
                prior++;
            }
            return prior >= 3;
        }
    }
}
