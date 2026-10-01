using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WalkingDog.Leaderboards
{
    public enum SocialTab { Activity, Discover, Following, Followers, Profile }

    public sealed class SocialPlayer
    {
        public string Id, Name, Photo;
        public bool Following, FollowsYou, SharesActivity;
    }

    public sealed class SocialActivity
    {
        public string Id;
        public SocialPlayer Player;
        public DateTime EndedUtc;
        public long Steps;
        public double DistanceMeters, DurationSeconds;
    }

    public sealed class SocialSnapshot
    {
        public SocialPlayer Me;
        public int FollowingCount, FollowerCount;
        public string Code;
        public List<SocialPlayer> Players = new List<SocialPlayer>();
        public List<SocialActivity> Activities = new List<SocialActivity>();
        public SocialPlayer Profile;
    }

    public interface ISocialService
    {
        string AuthenticatedUserId { get; }
        Task<SocialSnapshot> LoadSocialAsync(SocialTab tab, string target, CancellationToken token);
        Task SetFollowingAsync(string target, bool following, CancellationToken token);
        Task SetActivitySharingAsync(bool sharing, CancellationToken token);
    }

    internal static class SocialFeed
    {
        internal static List<SocialActivity> Recent(IEnumerable<SocialActivity> activities) => activities
            .GroupBy(a => (a.Player.Id, a.Id)).Select(g => g.First())
            .OrderByDescending(a => a.EndedUtc).ThenBy(a => a.Player.Id, StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal).Take(100).ToList();
    }
}
