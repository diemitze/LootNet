using System.Collections.Generic;
using System.Threading;

namespace LootNet.Services
{
    // Client-side snapshot of high-score records for the current team raid, populated by the Fika
    // companion from the server's list response. The team scoreboard reads this to draw record
    // markers and decide when to celebrate a record break.
    public static class HighScoreState
    {
        public readonly struct Individual
        {
            public readonly double Record; // best haul BEFORE this raid (the mark to beat)
            public readonly bool IsNew;     // this raid beat that record
            public Individual(double record, bool isNew) { Record = record; IsNew = isNew; }
        }

        public readonly struct TeamScore
        {
            public readonly double Record; // team's best combined haul before this raid
            public readonly double Live;    // this raid's combined haul
            public readonly bool IsNew;
            public TeamScore(double record, double live, bool isNew) { Record = record; Live = live; IsNew = isNew; }
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<string, Individual> _individual = new();
        private static TeamScore _team;
        private static int _version;

        public static int Version => Volatile.Read(ref _version);

        public static void SetFromServer(Dictionary<string, Individual> individual, TeamScore team)
        {
            lock (_lock)
            {
                _individual.Clear();
                if (individual != null)
                    foreach (var kv in individual) _individual[kv.Key] = kv.Value;
                _team = team;
            }
            Interlocked.Increment(ref _version);
        }

        public static Individual GetIndividual(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return default;
            lock (_lock) return _individual.TryGetValue(playerId, out var r) ? r : default;
        }

        public static TeamScore Team
        {
            get { lock (_lock) return _team; }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _individual.Clear();
                _team = default;
            }
            Interlocked.Increment(ref _version);
        }
    }
}
