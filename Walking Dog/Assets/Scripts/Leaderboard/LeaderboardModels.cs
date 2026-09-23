using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WalkingDog.Leaderboards
{
    public enum LeaderboardMetric { Distance, Steps } // the leaderboard score
    public enum LeaderboardScope { Global, Friends }

    public sealed class LeaderboardEntry
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string PhotoUrl { get; }
        public double TotalDistanceMeters { get; }
        public long TotalSteps { get; }
        // The spendable balance copied from Firebase, not a value derived from steps.
        public long PointsBalance { get; }
        public long CompletedWalkCount { get; }

        public LeaderboardEntry(string playerId, string displayName, double distanceMeters, long steps, long walkCount,
            string photoUrl = "", long pointsBalance = 0)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            PhotoUrl = photoUrl;
            TotalDistanceMeters = distanceMeters;
            TotalSteps = steps;
            PointsBalance = pointsBalance;
            CompletedWalkCount = walkCount;
        }

        internal static bool TryParse(string id, IDictionary<string, object> fields, out LeaderboardEntry entry) //parse function from the entry
        {
            entry = null;
            if (fields == null || !fields.TryGetValue("schemaVersion", out var version) || !(version is long v) || v != 1
                || !fields.TryGetValue("displayName", out var name) || !(name is string text) || !LeaderboardNames.IsValid(text)
                || !fields.TryGetValue("totalSteps", out var steps) || !(steps is long s) || s < 0 || s > 9007199254740991L
                || !fields.TryGetValue("completedWalkCount", out var count) || !(count is long c) || c < 1 || c > 9007199254740991L
                || !fields.TryGetValue("totalDistanceMeters", out var distance)) return false;
            double d;
            if (distance is double floating) d = floating;
            else if (distance is long integer) d = integer;
            else return false;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) return false; // return a false if its an errorr value
            // Old documents have no balance until their owner next syncs. Never
            // estimate it from steps because spending can lower the real balance.
            long points = 0;
            if (fields.TryGetValue("pointsBalance", out var savedPoints)
                && (!(savedPoints is long value) || value < 0 || value > 9007199254740991L)) return false;
            if (savedPoints is long parsedPoints) points = parsedPoints;
            entry = new LeaderboardEntry(id, text, d, s, c, "", points);
            return true;
        }
    }

    public sealed class LeaderboardSnapshot
    {
        public LeaderboardMetric Metric { get; }
        public LeaderboardScope Scope { get; }
        public string CurrentPlayerId { get; }
        public IReadOnlyList<LeaderboardEntry> Entries { get; }
        // Null until this player has a counted walk. This does not mean zero global rank.
        public LeaderboardEntry CurrentPlayer { get; }
        public string CurrentPlayerPhotoUrl { get; }

        public LeaderboardSnapshot(LeaderboardMetric metric, string currentPlayerId,
            IReadOnlyList<LeaderboardEntry> entries, LeaderboardEntry currentPlayer,
            LeaderboardScope scope = LeaderboardScope.Global, string currentPlayerPhotoUrl = "")
        {
            Metric = metric;
            Scope = scope;
            CurrentPlayerId = currentPlayerId;
            Entries = entries;
            CurrentPlayer = currentPlayer;
            CurrentPlayerPhotoUrl = currentPlayerPhotoUrl;
        }
    }

    public static class LeaderboardNames // for character display names type
    {
        // Same ASCII policy as backend and rules. Never derive names from email.
        public static bool IsValid(string value) => value != null
            && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9 _-]{2,23}\z");
    }

    // UI code can use a mock implementation without changing scenes or Firebase.
    public interface ILeaderboardService : IDisposable
    {
        string AuthenticatedUserId { get; }
        Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, CancellationToken cancellationToken);
        Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, LeaderboardScope scope, CancellationToken cancellationToken);
        Task SaveDisplayNameAsync(string displayName, CancellationToken cancellationToken);
    }
}
