using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkingDog.Leaderboards;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class FriendsTests
{
    [Test]
    public void ShareCodesAreShortAndNormalizeTypedFormatting()
    {
        for (int i = 0; i < 100; i++)
        {
            var code = FriendCodes.Create();
            Assert.That(code.Length, Is.EqualTo(8));
            Assert.That(FriendCodes.Normalize("  " + FriendCodes.Format(code).ToLowerInvariant() + "  "), Is.EqualTo(code));
        }
        Assert.That(FriendCodes.Normalize("bad/code"), Is.Null);
        Assert.That(FriendCodes.Normalize("ABCD-0123"), Is.Null);
    }

    [Test]
    public void PhotosOnlyAllowGoogleHttpsAndFallbackInitials()
    {
        const string photo = "https://lh3.googleusercontent.com/a/photo=s96-c";
        Assert.That(ProfilePhotos.Normalize(photo), Is.EqualTo(photo));
        foreach (var invalid in new[] { "http://lh3.googleusercontent.com/a", "https://evil.test/a", "https://googleusercontent.com.evil.test/a", "file:///photo", "https://user@lh3.googleusercontent.com/a", "https://lh3.googleusercontent.com:444/a" })
            Assert.That(ProfilePhotos.Normalize(invalid), Is.Empty);
        Assert.That(ProfilePhotos.Initials("Mochi Walker"), Is.EqualTo("MW"));
        Assert.That(ProfilePhotos.Initials(""), Is.EqualTo("?"));
    }

    [Test]
    public void UploadedThumbnailRendersAndRebindingClearsPreviousPicture()
    {
        var source = new Texture2D(4, 4);
        var row = new GameObject("Photo test", typeof(RectTransform), typeof(Image), typeof(ProfilePhotoUI));
        try
        {
            var upload = ProfilePhotos.UploadPrefix + Convert.ToBase64String(source.EncodeToJPG());
            Assert.That(ProfilePhotos.IsUpload(upload), Is.True);
            var photo = row.GetComponent<ProfilePhotoUI>();
            photo.Bind("first", "First Walker", upload, null);
            Assert.That(row.GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(row.transform.Find("Profile initials").gameObject.activeSelf, Is.False);
            photo.Bind("second", "Second Walker", "", null);
            Assert.That(row.GetComponent<Image>().sprite, Is.Null);
            Assert.That(row.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("SW"));
        }
        finally { UnityEngine.Object.DestroyImmediate(row); UnityEngine.Object.DestroyImmediate(source); }
    }

    [Test]
    public void RankingsIncludeSelfAndAcceptedFriendsOutsideGlobalTopFifty()
    {
        var players = Enumerable.Range(0, 60).Select(i => new LeaderboardEntry("stranger" + i, "Stranger", 10000, 10000, 1)).ToList();
        players.Add(new LeaderboardEntry("me", "My Walker", 1, 100, 1));
        players.Add(new LeaderboardEntry("friend", "My Friend", 2, 50, 1));
        players.Add(new LeaderboardEntry("pending", "Pending Friend", 100, 1000, 1));
        var distance = FriendsRanking.Build("me", LeaderboardMetric.Distance, new[] { "friend", "no-walks" }, players);
        Assert.That(distance.Entries.Select(p => p.PlayerId), Is.EqualTo(new[] { "friend", "me" }));
        Assert.That(distance.CurrentPlayer.PlayerId, Is.EqualTo("me"));
        Assert.That(distance.Scope, Is.EqualTo(LeaderboardScope.Friends));
        var steps = FriendsRanking.Build("me", LeaderboardMetric.Steps, new[] { "friend" }, players);
        Assert.That(steps.Entries.Select(p => p.PlayerId), Is.EqualTo(new[] { "me", "friend" }));
        var removed = FriendsRanking.Build("me", LeaderboardMetric.Steps, new string[0], players);
        Assert.That(removed.Entries.Select(p => p.PlayerId), Is.EqualTo(new[] { "me" }));
    }

    [Test]
    public void TiesAreDeterministicAndUnrankedPlayersAreNotInvented()
    {
        var players = new[] { new LeaderboardEntry("a", "Walker A", 5, 10, 1), new LeaderboardEntry("b", "Walker B", 5, 10, 1) };
        var result = FriendsRanking.Build("me", LeaderboardMetric.Distance, new[] { "a", "b" }, players);
        Assert.That(result.Entries.Select(p => p.PlayerId), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(result.CurrentPlayer, Is.Null);
    }

    [Test]
    public void SignedOutAndInvalidRequestsNeverReachFirestore()
    {
        using (var service = new FirebaseLeaderboardService(() => "", (_, __) => Task.FromResult<LeaderboardSnapshot>(null), (_, __) => Task.CompletedTask))
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadFriendsAsync(CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadAsync(LeaderboardMetric.Distance, LeaderboardScope.Friends, CancellationToken.None));
            Assert.ThrowsAsync<FriendRequestException>(() => service.ChangeFriendAsync("bad/code", FriendAction.Send, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.LoadAsync(LeaderboardMetric.Distance, (LeaderboardScope)99, CancellationToken.None));
        }
    }
}
