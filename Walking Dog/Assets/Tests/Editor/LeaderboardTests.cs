using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkingDog.Leaderboards;

public sealed class LeaderboardTests
{
    private static LeaderboardSnapshot Result(string uid, LeaderboardMetric metric) =>
        new LeaderboardSnapshot(metric, uid, new List<LeaderboardEntry>().AsReadOnly(), null);

    [Test]
    public async Task SignedOutAndCancelledCallsNeverReachFirebase()
    {
        int reads = 0, writes = 0;
        string uid = "";
        using (var service = new FirebaseLeaderboardService(() => uid,
            (owner, metric) => { reads++; return Task.FromResult(Result(owner, metric)); },
            (_, __) => { writes++; return Task.CompletedTask; }))
        using (var cancellation = new CancellationTokenSource())
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadAsync(LeaderboardMetric.Distance, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDisplayNameAsync("Walker", CancellationToken.None));
            uid = "alice";
            cancellation.Cancel();
            try { await service.LoadAsync(LeaderboardMetric.Steps, cancellation.Token); Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException) { }
            Assert.That(reads, Is.Zero);
            Assert.That(writes, Is.Zero);
        }
    }

    [Test]
    public async Task AccountSwitchDiscardsPendingResults()
    {
        string uid = "alice";
        var completion = new TaskCompletionSource<LeaderboardSnapshot>();
        using (var service = new FirebaseLeaderboardService(() => uid, (_, __) => completion.Task, (_, __) => Task.CompletedTask))
        {
            var pending = service.LoadAsync(LeaderboardMetric.Distance, CancellationToken.None);
            uid = "bob";
            completion.SetResult(Result("alice", LeaderboardMetric.Distance));
            try { await pending; Assert.Fail("Stale account data was returned."); }
            catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task TimeoutAndDisposalReleasePendingRequests()
    {
        var completion = new TaskCompletionSource<LeaderboardSnapshot>();
        using (var service = new FirebaseLeaderboardService(() => "alice", (_, __) => completion.Task,
            (_, __) => Task.CompletedTask, TimeSpan.FromMilliseconds(10)))
        {
            try { await service.LoadAsync(LeaderboardMetric.Distance, CancellationToken.None); Assert.Fail("Expected timeout."); }
            catch (TimeoutException) { }
        }
        var disposed = new FirebaseLeaderboardService(() => "alice", (_, __) => completion.Task, (_, __) => Task.CompletedTask);
        var request = disposed.LoadAsync(LeaderboardMetric.Steps, CancellationToken.None);
        disposed.Dispose();
        try { await request; Assert.Fail("Expected cancellation."); }
        catch (OperationCanceledException) { }
        Assert.That(disposed.AuthenticatedUserId, Is.Empty);
        completion.SetException(new InvalidOperationException("Late server failure"));
    }

    [Test]
    public async Task ForwardsSelectedMetricAndSavesOnlyValidNickname()
    {
        string saved = null;
        using (var service = new FirebaseLeaderboardService(() => "alice",
            (uid, metric) => Task.FromResult(Result(uid, metric)),
            (uid, name) => { saved = uid + ":" + name; return Task.CompletedTask; }))
        {
            var result = await service.LoadAsync(LeaderboardMetric.Steps, CancellationToken.None);
            Assert.That(result.Metric, Is.EqualTo(LeaderboardMetric.Steps));
            Assert.That(result.CurrentPlayer, Is.Null);
            await service.SaveDisplayNameAsync("Mochi Walker", CancellationToken.None);
            Assert.That(saved, Is.EqualTo("alice:Mochi Walker"));
            foreach (var bad in new[] { "Hi", "<b>Dog</b>", " Dog", "Dog\n", new string('x', 25) })
                Assert.ThrowsAsync<ArgumentException>(() => service.SaveDisplayNameAsync(bad, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.LoadAsync((LeaderboardMetric)99, CancellationToken.None));
        }
    }

    [Test]
    public void ParsesLargeTotalsAndRejectsInvalidCloudData()
    {
        var data = new Dictionary<string, object> {
            ["schemaVersion"] = 1L, ["displayName"] = "Walker-12345678", ["totalSteps"] = 3000000000L,
            ["totalDistanceMeters"] = 1234.5, ["completedWalkCount"] = 10L
        };
        Assert.That(LeaderboardEntry.TryParse("alice", data, out var entry), Is.True);
        Assert.That(entry.TotalSteps, Is.EqualTo(3000000000L));
        data["totalDistanceMeters"] = double.NaN;
        Assert.That(LeaderboardEntry.TryParse("alice", data, out _), Is.False);
        data["totalDistanceMeters"] = 100L;
        Assert.That(LeaderboardEntry.TryParse("alice", data, out _), Is.True);
        data["totalSteps"] = -1L;
        Assert.That(LeaderboardEntry.TryParse("alice", data, out _), Is.False);
    }
}
