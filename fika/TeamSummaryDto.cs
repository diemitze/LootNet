using System;
using System.Collections.Generic;
using LootNet.Services;

namespace LootNet.Fika
{

    public class TeamSummaryDto
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string MapName { get; set; }
        public double TotalFoundValue { get; set; }
        public int    ItemsFound { get; set; }
        public int    PmcKills { get; set; }
        public int    ScavKills { get; set; }
        public bool   IsScavRaid { get; set; }
        public bool   PlayerSurvived { get; set; }
        public int    XpEarned { get; set; }
        public int    XpBonus { get; set; }
        public List<string> TopItemNames { get; set; } = new();
        public List<double> TopItemValues { get; set; } = new();

        public static TeamSummaryDto From(RaidStats s, string playerId, string playerName)
        {
            var dto = new TeamSummaryDto
            {
                PlayerId        = playerId,
                PlayerName      = playerName ?? "Teammate",
                MapName         = s.MapName ?? "",
                TotalFoundValue = s.TotalFoundValue,
                ItemsFound      = s.ItemsFound,
                PmcKills        = s.PmcKills,
                ScavKills       = s.ScavKills,
                IsScavRaid      = s.IsScavRaid,
                PlayerSurvived  = s.PlayerSurvived,
                XpEarned        = s.XpEarned,
                XpBonus         = s.XpBonus,
            };

            if (s.TopItems != null)
            {
                int n = Math.Min(s.TopItems.Count, 5);
                for (int i = 0; i < n; i++)
                {
                    dto.TopItemNames.Add(s.TopItems[i].Name ?? "");
                    dto.TopItemValues.Add(s.TopItems[i].Value);
                }
            }
            return dto;
        }

        public RaidStats ToStats()
        {
            var stats = new RaidStats
            {
                PlayerName      = PlayerName,
                MapName         = MapName,
                TotalFoundValue = TotalFoundValue,
                ItemsFound      = ItemsFound,
                PmcKills        = PmcKills,
                ScavKills       = ScavKills,
                IsScavRaid      = IsScavRaid,
                PlayerSurvived  = PlayerSurvived,
                XpEarned        = XpEarned,
                XpBonus         = XpBonus,
                TopItems        = new List<(string, double)>(),
            };

            if (TopItemNames != null && TopItemValues != null)
            {
                int n = Math.Min(TopItemNames.Count, TopItemValues.Count);
                for (int i = 0; i < n; i++)
                    stats.TopItems.Add((TopItemNames[i], TopItemValues[i]));
            }
            return stats;
        }
    }
}
