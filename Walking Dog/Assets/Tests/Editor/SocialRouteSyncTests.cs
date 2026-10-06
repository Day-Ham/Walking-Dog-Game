using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkingDog.Leaderboards;

public sealed class SocialRouteSyncTests
{
    private static StepCountAndGpsManager.SavedWalkSession Walk(string id, string owner = "alice", bool synced = true)
        => new StepCountAndGpsManager.SavedWalkSession {
            id = id, ownerUserId = owner,
            sync = new WalkSyncMetadata { state = synced ? WalkUploadState.Synced : WalkUploadState.Pending },
            routePoints = new List<StepCountAndGpsManager.WalkRoutePoint> {
                new StepCountAndGpsManager.WalkRoutePoint { latitude = 14.5f, longitude = 121, startsNewSegment = true },
                new StepCountAndGpsManager.WalkRoutePoint { latitude = 14.6f, longitude = 121.1f, startsNewSegment = true }
            }
        };

    [Test]
    public async Task ToggleOnBackfillsOnlyOwnedSyncedRoutesAndNewWalksJoinLater()
    {
        var walks = new List<StepCountAndGpsManager.SavedWalkSession> {
            Walk("old"), Walk("other", "bob"), Walk("guest", ""), Walk("pending", synced: false), Walk("no-gps")
        };
        walks[4].routePoints.Clear();
        bool enabled = false;
        var uploads = new List<string>();
        var sync = new SocialRouteSync(() => "alice", () => walks, _ => Task.FromResult(enabled),
            (uid, id, json, token) => {
                Assert.That(uid, Is.EqualTo("alice"));
                Assert.That(SocialRouteCodec.Decode(json)[1].startsNewSegment, Is.True);
                uploads.Add(id); return Task.FromResult(true);
            });
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploads, Is.Empty);
        enabled = true;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploads, Is.EqualTo(new[] { "old" }));
        walks[3].sync.state = WalkUploadState.Synced;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploads, Is.EqualTo(new[] { "old", "pending" }));
        walks.Add(Walk("new")); enabled = false;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploads.Count, Is.EqualTo(2));
        enabled = true;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploads, Is.EqualTo(new[] { "old", "pending", "new" }));
    }

    [Test]
    public async Task FailedRouteRetriesWithoutBlockingOthersAndRestartReconcilesSavedWalks()
    {
        bool offline = true;
        var uploaded = new List<string>();
        var walks = new List<StepCountAndGpsManager.SavedWalkSession> { Walk("retry"), Walk("good") };
        Func<string, string, string, CancellationToken, Task<bool>> write = (uid, id, json, token) => {
            if (id == "retry" && offline) return Task.FromException<bool>(new TimeoutException());
            uploaded.Add(id); return Task.FromResult(true);
        };
        var sync = new SocialRouteSync(() => "alice", () => walks, _ => Task.FromResult(true), write);
        Assert.ThrowsAsync<InvalidOperationException>(() => sync.SyncAsync(CancellationToken.None));
        Assert.That(uploaded, Is.EqualTo(new[] { "good" }));
        offline = false;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(uploaded, Is.EqualTo(new[] { "good", "retry" }));
        var restarted = new SocialRouteSync(() => "alice", () => walks, _ => Task.FromResult(true), write);
        await restarted.SyncAsync(CancellationToken.None);
        Assert.That(uploaded.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task SharingRevokedDuringUploadDoesNotAcknowledgeOrPublishLaterWalks()
    {
        bool serverEnabled = false;
        int attempts = 0;
        var sync = new SocialRouteSync(() => "alice", () => new List<StepCountAndGpsManager.SavedWalkSession> { Walk("one"), Walk("two") },
            _ => Task.FromResult(true), (uid, id, json, token) => { attempts++; return Task.FromResult(serverEnabled); });
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(attempts, Is.EqualTo(1));
        serverEnabled = true;
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(attempts, Is.EqualTo(3)); // Both unacknowledged walks retry.
    }

    [Test]
    public async Task AccountSwitchDuringReadCannotPublishThePreviousAccountsRoutes()
    {
        string user = "alice";
        var read = new TaskCompletionSource<bool>();
        int writes = 0;
        var sync = new SocialRouteSync(() => user,
            () => new List<StepCountAndGpsManager.SavedWalkSession> { Walk("same"), Walk("same", "bob") },
            uid => uid == "alice" ? read.Task : Task.FromResult(true),
            (uid, id, json, token) => { writes++; Assert.That(uid, Is.EqualTo("bob")); return Task.FromResult(true); });
        var pending = sync.SyncAsync(CancellationToken.None);
        user = "bob"; read.SetResult(true);
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(writes, Is.Zero);
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(writes, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellingAnUploadLeavesItRetryableAndStopsTheRestOfTheBatch()
    {
        var native = new TaskCompletionSource<bool>();
        var cancelled = new CancellationTokenSource();
        int writes = 0;
        var sync = new SocialRouteSync(() => "alice",
            () => new List<StepCountAndGpsManager.SavedWalkSession> { Walk("one"), Walk("two") },
            _ => Task.FromResult(true), (uid, id, json, token) => ++writes == 1 ? native.Task : Task.FromResult(true));
        var pending = sync.SyncAsync(cancelled.Token);
        cancelled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(writes, Is.EqualTo(1));
        native.SetResult(true); // Native Firebase operations can finish after cancellation.
        await sync.SyncAsync(CancellationToken.None);
        Assert.That(writes, Is.EqualTo(3));
        cancelled.Dispose();
    }
}
