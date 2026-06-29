using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LootNet.Services
{

    public static class TeamSummaryStore
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, RaidStats> _summaries = new();
        private static int _version;

        public static int Version => Volatile.Read(ref _version);

        public static event Action OnRefreshRequested;

        public static bool HasAny { get { lock (_lock) return _summaries.Count > 0; } }
        public static int Count { get { lock (_lock) return _summaries.Count; } }

        public static List<RaidStats> Snapshot()
        {
            lock (_lock) return _summaries.Values.ToList();
        }

        public static List<KeyValuePair<string, RaidStats>> SnapshotPairs()
        {
            lock (_lock) return _summaries.ToList();
        }

        public static void Submit(string senderId, RaidStats stats)
        {
            if (string.IsNullOrEmpty(senderId) || stats == null) return;

            bool changed;
            lock (_lock)
            {
                changed = !_summaries.TryGetValue(senderId, out var existing) || !SameContent(existing, stats);
                if (changed) _summaries[senderId] = stats;
            }
            if (changed) Interlocked.Increment(ref _version);
        }

        private static bool SameContent(RaidStats a, RaidStats b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.TotalFoundValue != b.TotalFoundValue ||
                a.ItemsFound      != b.ItemsFound      ||
                a.PmcKills        != b.PmcKills        ||
                a.ScavKills       != b.ScavKills       ||
                a.XpEarned        != b.XpEarned        ||
                a.XpBonus         != b.XpBonus         ||
                a.PlayerSurvived  != b.PlayerSurvived  ||
                a.PlayerName      != b.PlayerName      ||
                a.MapName         != b.MapName)
                return false;

            int an = a.TopItems?.Count ?? 0, bn = b.TopItems?.Count ?? 0;
            if (an != bn) return false;
            for (int i = 0; i < an; i++)
                if (a.TopItems[i].Name != b.TopItems[i].Name || a.TopItems[i].Value != b.TopItems[i].Value)
                    return false;
            return true;
        }

        public static void Clear()
        {
            lock (_lock)
            {
                if (_summaries.Count == 0) return;
                _summaries.Clear();
            }
            Interlocked.Increment(ref _version);
        }

        public static void RequestRefresh()
        {
            try { OnRefreshRequested?.Invoke(); } catch { }
        }
    }
}
