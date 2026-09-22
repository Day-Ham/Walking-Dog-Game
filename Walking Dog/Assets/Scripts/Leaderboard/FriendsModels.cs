using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WalkingDog.Leaderboards
{
    public enum FriendAction { Send, Accept, Remove }

    public interface IPlayerProfileService
    {
        // Empty resets the custom picture to the Google account photo (or initials).
        Task SaveProfilePhotoAsync(string uploadedPhoto, CancellationToken token);
    }

    public sealed class FriendEntry
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string RequestedBy { get; }
        public bool Accepted { get; }
        public string PhotoUrl { get; }
        public FriendEntry(string playerId, string displayName, string requestedBy, bool accepted, string photoUrl = "")
        { PlayerId = playerId; DisplayName = displayName; RequestedBy = requestedBy; Accepted = accepted; PhotoUrl = photoUrl; }
    }

    public interface IFriendsService
    {
        string AuthenticatedUserId { get; }
        Task<string> GetFriendCodeAsync(CancellationToken token);
        Task<IReadOnlyList<FriendEntry>> LoadFriendsAsync(CancellationToken token);
        Task ChangeFriendAsync(string friendCode, FriendAction action, CancellationToken token);
    }

    internal static class FriendCodes
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        internal static string Create()
        {
            var bytes = new byte[8];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
        }
        internal static string Normalize(string value)
        {
            var code = (value ?? "").Trim().Replace("-", "").ToUpperInvariant();
            return code.Length == 8 && code.All(c => Alphabet.Contains(c)) ? code : null;
        }
        internal static string Format(string code) => code.Insert(4, "-");
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
