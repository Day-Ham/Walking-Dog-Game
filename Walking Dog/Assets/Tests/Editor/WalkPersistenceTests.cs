using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Walk = StepCountAndGpsManager.SavedWalkSession;

public sealed class WalkPersistenceTests
{
    private string directory;
    private LocalWalkRepository repository;
    private DateTimeOffset now;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "WalkingDogTests", Guid.NewGuid().ToString("N"));
        repository = new LocalWalkRepository(directory, _ => { });
        now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
    }

    [TearDown]
    public void TearDown()
    {
        // This exact directory was created solely by this test's random ID.
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Test]
    public void CompletedWalkSurvivesRestartWithAllMeasurementsAndRoute()
    {
        repository.Save(MakeWalk("one"));
        var restored = Restart().LoadAll();
        Assert.That(restored.Count, Is.EqualTo(1));
        Assert.That(restored[0].steps, Is.EqualTo(120));
        Assert.That(restored[0].distanceMeters, Is.EqualTo(75.5f));
        Assert.That(restored[0].durationSeconds, Is.EqualTo(60));
        Assert.That(restored[0].routePoints[0].latitude, Is.EqualTo(14.5f));
        Assert.That(restored[0].routePointCount, Is.EqualTo(1));
        Assert.That(restored[0].schemaVersion, Is.EqualTo(1));
        Assert.That(restored[0].sync.state, Is.EqualTo(WalkUploadState.Pending));
    }

    [Test]
    public void RepeatedSaveUsesOneFileAndMultipleWalksStaySeparate()
    {
        var path = repository.Save(MakeWalk("one"));
        Assert.That(repository.Save(MakeWalk("one")), Is.EqualTo(path));
        repository.Save(MakeWalk("two"));
        Assert.That(repository.GetFilePaths().Count, Is.EqualTo(2));
        Assert.That(Restart().LoadAll().Count, Is.EqualTo(2));
    }

    [Test]
    public void CompletedPayloadCannotBeOverwritten()
    {
        repository.Save(MakeWalk("one"));
        var changed = MakeWalk("one");
        changed.steps++;
        Assert.Throws<InvalidOperationException>(() => repository.Save(changed));
        Assert.That(Restart().Find("one").steps, Is.EqualTo(120));
    }

    [Test]
    public void LegacyJsonLoadsWithoutRewritingAndDeduplicatesTimestampFiles()
    {
        Directory.CreateDirectory(directory);
        var legacy = "{\"id\":\"legacy\",\"startedAtUtc\":\"2026-09-08T00:00:00Z\",\"endedAtUtc\":\"2026-09-08T00:01:00Z\",\"steps\":12,\"distanceMeters\":8,\"durationSeconds\":60,\"routePointCount\":0}";
        var oldPath = Path.Combine(directory, "walk_20260908_000100_legacy.json");
        File.WriteAllText(oldPath, legacy);
        File.WriteAllText(Path.Combine(directory, "walk_20260908_000101_legacy.json"), legacy);
        var walks = repository.LoadAll();
        Assert.That(walks.Count, Is.EqualTo(1));
        Assert.That(walks[0].schemaVersion, Is.EqualTo(1));
        Assert.That(walks[0].ownerUserId, Is.Empty);
        Assert.That(walks[0].sync.state, Is.EqualTo(WalkUploadState.Pending));
        Assert.That(File.ReadAllText(oldPath), Is.EqualTo(legacy));
        repository.AssignOwner("legacy", "alice");
        Assert.That(Restart().LoadAll().Count, Is.EqualTo(1));
        Assert.That(Restart().Find("legacy").ownerUserId, Is.EqualTo("alice"));
    }

    [Test]
    public void MissingCorruptIncompleteAndFutureFilesDoNotHideHealthyWalks()
    {
        Assert.That(repository.LoadAll(), Is.Empty);
        Assert.That(repository.TryLoad(Path.Combine(directory, "missing.json"), out _), Is.False);
        repository.Save(MakeWalk("healthy"));
        File.WriteAllText(Path.Combine(directory, "walk_broken.json"), "{broken");
        File.WriteAllText(Path.Combine(directory, "walk_empty.json"), "{}");
        var future = MakeWalk("future");
        future.schemaVersion = 999;
        File.WriteAllText(Path.Combine(directory, "walk_future.json"), JsonUtility.ToJson(future));
        Assert.That(Restart().LoadAll().Count, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(directory, "walk_broken.json")), Is.True);
        Assert.Throws<IOException>(() => repository.Save(MakeWalk("future")));
    }

    [Test]
    public void FuturePrimaryIsNotRolledBackToAnOlderBackup()
    {
        var path = repository.Save(MakeWalk("one"));
        repository.AssignOwner("one", "alice");
        var future = MakeWalk("one");
        future.schemaVersion = 999;
        var futureJson = JsonUtility.ToJson(future);
        File.WriteAllText(path, futureJson);
        Assert.That(repository.TryLoad(path, out _), Is.False);
        Assert.Throws<IOException>(() => repository.Save(MakeWalk("one")));
        // Even a duplicate legacy record cannot overwrite the future primary.
        File.WriteAllText(Path.Combine(directory, "walk_legacy_one.json"), JsonUtility.ToJson(MakeWalk("one")));
        Assert.Throws<IOException>(() => repository.AssignOwner("one", "alice"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(futureJson));
    }

    [Test]
    public void BackupRecoversCorruptionAndUncommittedTemporaryFileIsIgnored()
    {
        var path = repository.Save(MakeWalk("one", ""));
        repository.AssignOwner("one", "alice");
        File.WriteAllText(path, "truncated");
        File.WriteAllText(path + ".tmp", "partial replacement");
        var recovered = Restart().LoadAll();
        Assert.That(recovered.Count, Is.EqualTo(1));
        Assert.That(recovered[0].id, Is.EqualTo("one"));
        Assert.That(recovered[0].steps, Is.EqualTo(120));
        // A backup may predate ownership/sync changes, so recovery must be safe
        // to repeat rather than claim data or issue rewards again.
        Assert.That(recovered[0].ownerUserId, Is.Empty);
        repository.AssignOwner("one", "alice");
        Assert.That(Restart().Find("one").ownerUserId, Is.EqualTo("alice"));
    }

    [Test]
    public void BackupAlsoLoadsWhenPrimaryIsMissing()
    {
        var path = repository.Save(MakeWalk("one", ""));
        repository.AssignOwner("one", "alice");
        File.Delete(path);
        Assert.That(Restart().LoadAll().Count, Is.EqualTo(1));
    }

    [Test]
    public void InvalidPathsAndMeasurementsAreRejected()
    {
        var bad = MakeWalk("../escape");
        Assert.Throws<ArgumentException>(() => repository.Save(bad));
        bad = MakeWalk("one");
        bad.distanceMeters = float.NaN;
        Assert.Throws<ArgumentException>(() => repository.Save(bad));
        bad = MakeWalk("one");
        bad.routePoints[0].longitude = 200;
        Assert.Throws<ArgumentException>(() => repository.Save(bad));
        Assert.That(repository.TryLoad(Path.Combine(directory, "..", "outside.json"), out _), Is.False);
    }

    [Test]
    public void DiskFailureIsReportedInsteadOfPretendingSaveSucceeded()
    {
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "existing file");
        Assert.Throws<IOException>(() => new LocalWalkRepository(blocker).Save(MakeWalk("one")));
        Assert.That(File.ReadAllText(blocker), Is.EqualTo("existing file"));
    }

    [Test]
    public async Task FailedUploadSurvivesRestartAndRetriesAfterBackoff()
    {
        repository.Save(MakeWalk("one"));
        var cloud = new FakeCloud { FailBeforeCommit = true };
        await Service(cloud).SyncPendingAsync();
        var failed = Restart().Find("one");
        Assert.That(failed.sync.state, Is.EqualTo(WalkUploadState.RetryNeeded));
        Assert.That(failed.sync.attempts, Is.EqualTo(1));
        cloud.FailBeforeCommit = false;
        await new WalkSyncService(Restart(), cloud, () => now).SyncPendingAsync();
        Assert.That(cloud.Calls, Is.EqualTo(1));
        now = now.AddSeconds(6);
        await new WalkSyncService(Restart(), cloud, () => now).SyncPendingAsync();
        Assert.That(Restart().Find("one").sync.state, Is.EqualTo(WalkUploadState.Synced));
        Assert.That(cloud.Documents.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task LostAcknowledgementRetryUpsertsWithoutDuplicateAndResaveKeepsSyncedState()
    {
        repository.Save(MakeWalk("one"));
        var cloud = new FakeCloud { LoseAcknowledgement = true };
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Documents.Count, Is.EqualTo(1));
        Assert.That(Restart().Find("one").sync.state, Is.EqualTo(WalkUploadState.RetryNeeded));
        cloud.LoseAcknowledgement = false;
        now = now.AddSeconds(6);
        await Service(cloud).SyncPendingAsync();
        repository.Save(MakeWalk("one"));
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Calls, Is.EqualTo(2));
        Assert.That(cloud.Documents.Count, Is.EqualTo(1));
        Assert.That(Restart().Find("one").sync.state, Is.EqualTo(WalkUploadState.Synced));
    }

    [Test]
    public async Task SignedOutUnownedAndOtherAccountsAreNeverUploaded()
    {
        repository.Save(MakeWalk("unowned", ""));
        repository.Save(MakeWalk("alice-walk"));
        repository.Save(MakeWalk("bob-walk", "bob"));
        var cloud = new FakeCloud { UserId = "" };
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Calls, Is.Zero);
        cloud.UserId = "alice";
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Documents.Keys, Is.EquivalentTo(new[] { "alice/alice-walk" }));
        repository.AssignOwner("unowned", "alice");
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Documents.Count, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => repository.AssignOwner("bob-walk", "alice"));
    }

    [Test]
    public async Task AccountSwitchDuringUploadStopsRemainingQueue()
    {
        repository.Save(MakeWalk("one"));
        repository.Save(MakeWalk("two"));
        var cloud = new FakeCloud();
        cloud.AfterCommit = () => cloud.UserId = "bob";
        await Service(cloud).SyncPendingAsync();
        Assert.That(cloud.Calls, Is.EqualTo(1));
        Assert.That(repository.LoadAll().FindAll(walk => walk.sync.state == WalkUploadState.Synced).Count, Is.EqualTo(1));
        Assert.That(cloud.Documents.Keys, Has.All.StartsWith("alice/"));
    }

    [Test]
    public async Task ConcurrentRequestsShareOneUploadAndCancellationKeepsRetry()
    {
        repository.Save(MakeWalk("one"));
        var gate = new TaskCompletionSource<bool>();
        var cloud = new FakeCloud { Gate = gate.Task };
        var service = Service(cloud);
        using (var cancellation = new CancellationTokenSource())
        {
            var first = service.SyncPendingAsync(cancellation.Token);
            await service.SyncPendingAsync();
            Assert.That(cloud.Calls, Is.EqualTo(1));
            cancellation.Cancel();
            gate.SetResult(true);
            try { await first; Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException) { }
        }
        Assert.That(service.IsRunning, Is.False);
        Assert.That(Restart().Find("one").sync.state, Is.EqualTo(WalkUploadState.RetryNeeded));
    }

    [Test]
    public async Task SummaryPayloadDoesNotContainGpsOrQueueMetadata()
    {
        repository.Save(MakeWalk("one"));
        var cloud = new FakeCloud();
        await Service(cloud).SyncPendingAsync();
        var payload = JsonUtility.ToJson(cloud.Documents["alice/one"]);
        Assert.That(payload, Does.Not.Contain("latitude").IgnoreCase);
        Assert.That(payload, Does.Not.Contain("longitude").IgnoreCase);
        Assert.That(payload, Does.Not.Contain("routePoints"));
        Assert.That(payload, Does.Not.Contain("sync"));
        Assert.That(cloud.Documents["alice/one"].steps, Is.EqualTo(120));
    }

    [Test]
    public void ManagerEndWalkSavesAndReloadsThroughRepository()
    {
        var gameObject = new GameObject("Walk persistence integration test");
        try
        {
            var manager = gameObject.AddComponent<StepCountAndGpsManager>();
            typeof(StepCountAndGpsManager).GetField("localWalks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, repository);
            manager.SetStep(50);
            manager.SetGpsLocation(14.5f, 121, 5);
            manager.BeginWalkingSession();
            manager.SetStep(170);
            manager.EndWalkingSession();
            Assert.That(manager.LastWalkSaveState, Is.EqualTo("Saved on device"));
            Assert.That(Restart().LoadAll()[0].steps, Is.EqualTo(120));
            Assert.That(Restart().LoadAll()[0].routePointCount, Is.EqualTo(1));
            manager.SetGpsLocation(14.6f, 121.1f, 8);
            manager.SetStep(180);
            manager.SaveCurrentWalkingSession();
            Assert.That(repository.GetFilePaths().Count, Is.EqualTo(1));
            Assert.That(manager.LoadSavedWalks().Count, Is.EqualTo(1));
            Assert.That(Restart().LoadAll()[0].finalLatitude, Is.EqualTo(14.5f));
            Assert.That(Restart().LoadAll()[0].steps, Is.EqualTo(120));
        }
        finally { UnityEngine.Object.DestroyImmediate(gameObject); }
    }

    private LocalWalkRepository Restart() => new LocalWalkRepository(directory, _ => { });
    private WalkSyncService Service(FakeCloud cloud) => new WalkSyncService(repository, cloud, () => now);

    private static Walk MakeWalk(string id, string owner = "alice")
    {
        return new Walk
        {
            id = id,
            ownerUserId = owner,
            startedAtUtc = "2026-09-08T00:00:00Z",
            endedAtUtc = "2026-09-08T00:01:00Z",
            steps = 120,
            distanceMeters = 75.5f,
            durationSeconds = 60,
            routePointCount = 1,
            routePoints = new List<StepCountAndGpsManager.WalkRoutePoint>
            {
                new StepCountAndGpsManager.WalkRoutePoint { latitude = 14.5f, longitude = 121, accuracyMeters = 5 }
            }
        };
    }

    // Test-only adapter. This assembly is excluded from player builds.
    private sealed class FakeCloud : IWalkCloudStore
    {
        public string UserId = "alice";
        public string AuthenticatedUserId => UserId;
        public bool FailBeforeCommit;
        public bool LoseAcknowledgement;
        public int Calls;
        public Action AfterCommit;
        public Task Gate;
        public readonly Dictionary<string, WalkSummary> Documents = new Dictionary<string, WalkSummary>();

        public async Task UploadSummaryAsync(string ownerUserId, WalkSummary summary, CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate != null) await Gate;
            cancellationToken.ThrowIfCancellationRequested();
            if (ownerUserId != UserId) throw new InvalidOperationException("Wrong account.");
            if (FailBeforeCommit) throw new IOException("Simulated offline state.");
            Documents[ownerUserId + "/" + summary.id] = summary;
            AfterCommit?.Invoke();
            if (LoseAcknowledgement) throw new IOException("Simulated lost acknowledgement.");
        }
    }
}
