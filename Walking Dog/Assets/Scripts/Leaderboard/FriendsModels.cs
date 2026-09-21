using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WalkingDog.Leaderboards
{
    public enum FriendAction { Send, Accept, Remove }

    public sealed class FriendEntry
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string RequestedBy { get; }
        public bool Accepted { get; }
        public FriendEntry(string playerId, string displayName, string requestedBy, bool accepted)
        { PlayerId = playerId; DisplayName = displayName; RequestedBy = requestedBy; Accepted = accepted; }
    }

    public interface IFriendsService
    {
        string AuthenticatedUserId { get; }
        Task<IReadOnlyList<FriendEntry>> LoadFriendsAsync(CancellationToken token);
        Task ChangeFriendAsync(string friendCode, FriendAction action, CancellationToken token);
    }

    public sealed class FriendRequestException : Exception
    {
        public FriendRequestException(string message) : base(message) { }
    }

    internal static class FriendsRanking
    {
        internal static LeaderboardSnapshot Build(string uid, LeaderboardMetric metric,
            IEnumerable<string> acceptedIds, IEnumerable<LeaderboardEntry> players)
        {
            var allowed = new HashSet<string>(acceptedIds, StringComparer.Ordinal) { uid };
            var eligible = players.Where(p => allowed.Contains(p.PlayerId)).GroupBy(p => p.PlayerId).Select(g => g.First());
            var ordered = metric == LeaderboardMetric.Distance
                ? eligible.OrderByDescending(p => p.TotalDistanceMeters)
                : eligible.OrderByDescending(p => p.TotalSteps);
            var entries = ordered.ThenByDescending(p => p.PlayerId, StringComparer.Ordinal).ToList();
            return new LeaderboardSnapshot(metric, uid, entries.AsReadOnly(),
                entries.FirstOrDefault(p => p.PlayerId == uid), LeaderboardScope.Friends);
        }
    }
}
