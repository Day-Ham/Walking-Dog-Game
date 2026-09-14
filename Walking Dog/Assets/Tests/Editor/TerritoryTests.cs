using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Walk = StepCountAndGpsManager.SavedWalkSession;

public sealed class TerritoryTests
{
    private string directory;
    [SetUp] public void Setup() => directory = Path.Combine(Path.GetTempPath(), "WalkingDogTerritoryTests", Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test] public void SquareClaimsNineStableTilesInEitherDirection()
    {
        var forward = TerritoryCapture.Evaluate(Square());
        var reverse = Square(); reverse.routePoints.Reverse(); Retime(reverse);
        Assert.That(forward.tiles.Count, Is.EqualTo(9), forward.message);
        CollectionAssert.AreEquivalent(forward.tiles, TerritoryCapture.Evaluate(reverse).tiles);
        Assert.That(forward.tiles, Does.Contain(new TerritoryCapture.Tile(269360, 32800)));
    }

    [Test] public void ConcaveLoopDoesNotFillItsMissingCorner()
    {
        var result = TerritoryCapture.Evaluate(Polygon(0, 0, 150, 0, 150, 50, 50, 50, 50, 150, 0, 150, 0, 0));
        Assert.That(result.tiles.Count, Is.EqualTo(5), result.message);
        Assert.That(result.tiles, Has.No.Member(new TerritoryCapture.Tile(269362, 32802)));
    }

    [Test] public void OpenLoopRejectsAndSmallClosingGapIsAccepted()
    {
        var open = Square(); open.routePoints.RemoveAt(open.routePoints.Count - 1);
        Assert.That(TerritoryCapture.Evaluate(open).message, Does.Contain("within 50 m"));
        var near = Polygon(0, 0, 150, 0, 150, 150, 0, 150, 0, 15);
        Assert.That(TerritoryCapture.Evaluate(near).tiles.Count, Is.EqualTo(9));
    }

    [Test] public void SelfCrossingRetracedAndTinyRoutesCannotClaim()
    {
        Assert.That(TerritoryCapture.Evaluate(Polygon(0, 0, 150, 150, 0, 150, 150, 0, 0, 0)).Accepted, Is.False);
        Assert.That(TerritoryCapture.Evaluate(Polygon(0, 0, 150, 0, 300, 0, 150, 0, 0, 0)).Accepted, Is.False);
        Assert.That(TerritoryCapture.Evaluate(Polygon(0, 0, 20, 0, 20, 20, 0, 20, 0, 0)).message, Does.Contain("200 m"));
        Assert.That(TerritoryCapture.Evaluate(Polygon(0, 0, 150, 0, 150, 10, 0, 10, 0, 0)).message, Does.Contain("2,500"));
    }

    [Test] public void LegacyGappedAndInvalidGpsRoutesAreRejected()
    {
        var walk = Square(); walk.territoryVersion = 0;
        Assert.That(TerritoryCapture.Evaluate(walk).Accepted, Is.False);
        walk = Square(); walk.hasTrackingGaps = true;
        Assert.That(TerritoryCapture.Evaluate(walk).message, Does.Contain("GPS gaps"));
        walk = Square(); walk.routePoints[2].startsNewSegment = true;
        Assert.That(TerritoryCapture.Evaluate(walk).Accepted, Is.False);
        walk = Square(); walk.routePoints[2].latitude = float.NaN;
        Assert.That(TerritoryCapture.Evaluate(walk).Accepted, Is.False);
        walk = Square(); walk.routePoints[2].gpsTimestamp = walk.routePoints[1].gpsTimestamp;
        Assert.That(TerritoryCapture.Evaluate(walk).Accepted, Is.False);
        walk = Square(); walk.routePoints[2].accuracyMeters = 20;
        Assert.That(TerritoryCapture.Evaluate(walk).Accepted, Is.False);
        walk = Square(); walk.routePoints[1].gpsTimestamp = walk.routePoints[0].gpsTimestamp + 1;
        Assert.That(TerritoryCapture.Evaluate(walk).message, Does.Contain("GPS jump"));
    }

    [Test] public void LargeAndUnsupportedLatitudeLoopsAreBounded()
    {
        var large = Polygon(0, 0, 5100, 0, 5100, 5100, 0, 5100, 0, 0);
        for (int i = 0; i < large.routePoints.Count; i++) large.routePoints[i].gpsTimestamp += i * 2000;
        Assert.That(TerritoryCapture.Evaluate(large).message, Does.Contain("too large"));
        var polar = Square(); foreach (var p in polar.routePoints) p.latitude = 80;
        Assert.That(TerritoryCapture.Evaluate(polar).Accepted, Is.False);
    }

    [Test] public void DuplicateRetryRestartAndOverlapNeverAwardTwice()
    {
        var repository = new LocalTerritoryRepository(directory, "alice");
        var first = Square(); var award = repository.Apply(first);
        Assert.That(award.newTiles.Count, Is.EqualTo(9));
        Assert.That(repository.Apply(first), Is.SameAs(award));
        repository = new LocalTerritoryRepository(directory, "alice");
        Assert.That(repository.Apply(first).newTiles.Count, Is.EqualTo(9), "A retry preserves the original receipt, without adding tiles.");
        Assert.That(repository.Tiles.Count, Is.EqualTo(9));
        Assert.That(repository.Apply(Square()).newTiles.Count, Is.Zero);
        var overlapping = Polygon(100, 0, 250, 0, 250, 150, 100, 150, 100, 0);
        Assert.That(repository.Apply(overlapping).newTiles.Count, Is.EqualTo(6));
        Assert.That(new LocalTerritoryRepository(directory, "alice").Tiles.Count, Is.EqualTo(15));
    }

    [Test] public void OwnersAreIsolatedAndIdsCannotChangePaths()
    {
        var alice = new LocalTerritoryRepository(directory, "alice"); alice.Apply(Square());
        var bob = new LocalTerritoryRepository(directory, "bob");
        Assert.That(bob.Tiles.Count, Is.Zero);
        Assert.Throws<ArgumentException>(() => bob.Apply(Square()));
        var unusual = new LocalTerritoryRepository(directory, "../../unusual-owner");
        Assert.That(Path.GetDirectoryName(unusual.FilePath), Is.EqualTo(directory));
    }

    [Test] public void CorruptPrimaryRecoversBackupAndSavedWalkReplayRestoresLatestClaim()
    {
        var walks = new LocalWalkRepository(directory);
        var first = Square(); walks.Save(first);
        var service = new TerritoryService(walks); service.Refresh("alice");
        var second = Polygon(100, 0, 250, 0, 250, 150, 100, 150, 100, 0); walks.Save(second); service.Refresh("alice");
        var path = new LocalTerritoryRepository(Path.Combine(directory, "Territories"), "alice").FilePath;
        File.WriteAllText(path, "truncated");
        service = new TerritoryService(walks); service.Refresh("alice");
        Assert.That(service.Error, Is.Empty);
        Assert.That(service.Tiles.Count, Is.EqualTo(15));
        Assert.That(new LocalTerritoryRepository(Path.Combine(directory, "Territories"), "alice").Tiles.Count, Is.EqualTo(15));
    }

    [Test] public void FailedCommitDoesNotAwardInMemoryAndRetryWorks()
    {
        var repository = new LocalTerritoryRepository(directory, "alice");
        Directory.CreateDirectory(repository.FilePath + ".tmp"); // Deterministic write failure.
        Assert.Catch(() => repository.Apply(Square()));
        Assert.That(repository.Tiles.Count, Is.Zero);
        Directory.Delete(repository.FilePath + ".tmp");
        Assert.That(repository.Apply(Square()).newTiles.Count, Is.EqualTo(9));
    }

    [Test] public void CorruptOrFutureLedgersArePreserved()
    {
        var repository = new LocalTerritoryRepository(directory, "alice"); repository.Apply(Square()); repository.Apply(Square());
        File.WriteAllText(repository.FilePath, "broken-primary"); File.WriteAllText(repository.FilePath + ".bak", "broken-backup");
        Assert.Catch(() => new LocalTerritoryRepository(directory, "alice"));
        Assert.That(File.ReadAllText(repository.FilePath), Is.EqualTo("broken-primary"));
        File.WriteAllText(repository.FilePath, "{\"version\":2,\"owner\":\"alice\",\"claims\":[]}");
        Assert.Throws<NotSupportedException>(() => new LocalTerritoryRepository(directory, "alice"));
    }

    [Test] public void StartupReplaysOnlyNewCompletedOwnedWalksAndClearsOnSignOut()
    {
        var walks = new LocalWalkRepository(directory);
        var old = Square(); old.territoryVersion = 0; walks.Save(old);
        var other = Square(); other.ownerUserId = "bob"; walks.Save(other);
        var drafts = new LocalWalkRepository(Path.Combine(directory, "Active")); drafts.SaveCheckpoint(Square());
        var service = new TerritoryService(walks); service.Refresh("alice");
        Assert.That(service.Tiles.Count, Is.Zero);
        var walk = Square(); walks.Save(walk); // Simulates shutdown between completed walk and claim commit.
        service = new TerritoryService(walks); service.Refresh("alice");
        Assert.That(service.Tiles.Count, Is.EqualTo(9));
        Assert.That(service.Summary(walk.id, "alice", false), Does.Contain("+9"));
        var revision = service.Revision; service.Refresh("");
        Assert.That(service.Tiles.Count, Is.Zero); Assert.That(service.Revision, Is.Not.EqualTo(revision));
        service.Refresh("bob"); Assert.That(service.Tiles.Count, Is.EqualTo(9));
        Assert.That(service.Summary(walk.id, "alice", false), Does.Contain("Sign in"));
    }

    private static Walk Square() => Polygon(0, 0, 150, 0, 150, 150, 0, 150, 0, 0);

    [Test] public void TerritoryWriteFailureKeepsCompletedWalkAndRetriesLater()
    {
        var walks = new LocalWalkRepository(directory); var walk = Square(); walks.Save(walk);
        File.WriteAllText(Path.Combine(directory, "Territories"), "blocked");
        var service = new TerritoryService(walks); service.Refresh("alice");
        Assert.That(service.Error, Does.Contain("pending"));
        Assert.That(service.Tiles.Count, Is.Zero); Assert.That(walks.Find(walk.id), Is.Not.Null);
        File.Delete(Path.Combine(directory, "Territories")); service.Refresh("alice");
        Assert.That(service.Error, Is.Empty); Assert.That(service.Tiles.Count, Is.EqualTo(9));
    }

    [Test] public void StoppingWalkCommitsClaimAndRepeatedSavePreservesReceipt()
    {
        var host = new GameObject("Territory integration test");
        try
        {
            var manager = host.AddComponent<StepCountAndGpsManager>();
            Set(manager, "localWalks", new LocalWalkRepository(directory));
            manager.ConfigureCloudSync(new TestOwner());
            manager.BeginWalkingSession();
            var fixture = Square();
            Set(manager, "routePointSamples", fixture.routePoints);
            var route = new List<Vector2>(); foreach (var point in fixture.routePoints) route.Add(new Vector2(point.latitude, point.longitude));
            Set(manager, "routePoints", route);
            Set(manager, "sessionStartTime", Time.realtimeSinceStartup - 600);
            Set(manager, "sessionDistanceMeters", 600f);
            manager.EndWalkingSession();
            Assert.That(manager.HasUnsavedCompletedWalk, Is.False);
            Assert.That(manager.Territories.Tiles.Count, Is.EqualTo(9));
            Assert.That(manager.TerritorySummary, Does.Contain("+9"));
            manager.SaveCurrentWalkingSession();
            Assert.That(manager.Territories.Tiles.Count, Is.EqualTo(9));
            Assert.That(manager.LocalWalks.Find(manager.CurrentSessionId).territoryVersion, Is.EqualTo(1));
            manager.ConfigureCloudSync(null);
            Assert.That(manager.Territories.Tiles.Count, Is.Zero);
            manager.BeginWalkingSession(); manager.EndWalkingSession();
            Assert.That(manager.LocalWalks.Find(manager.CurrentSessionId).territoryVersion, Is.Zero, "Unsigned walks must not later be imported as eligible claims.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private sealed class TestOwner : IWalkCloudStore
    {
        public string AuthenticatedUserId => "alice";
        public System.Threading.Tasks.Task UploadSummaryAsync(string owner, WalkSummary summary, System.Threading.CancellationToken cancellation) => System.Threading.Tasks.Task.CompletedTask;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);
    private static Walk Polygon(params double[] coordinates)
    {
        var walk = new Walk { id = Guid.NewGuid().ToString("N"), ownerUserId = "alice", territoryVersion = 1, trackingVersion = 1,
            startedAtUtc = "2026-09-14T00:00:00Z", endedAtUtc = "2026-09-14T00:10:00Z", durationSeconds = 600,
            distanceMeters = 600, routePoints = new List<StepCountAndGpsManager.WalkRoutePoint>() };
        for (int i = 0; i < coordinates.Length; i += 2)
        {
            double x = 269360 * 50 + 5 + coordinates[i], y = 32800 * 50 + 5 + coordinates[i + 1];
            walk.routePoints.Add(new StepCountAndGpsManager.WalkRoutePoint { longitude = (float)(x / TerritoryCapture.Radius * 180 / Math.PI),
                latitude = (float)((2 * Math.Atan(Math.Exp(y / TerritoryCapture.Radius)) - Math.PI / 2) * 180 / Math.PI), accuracyMeters = 5 });
        }
        Retime(walk); walk.routePointCount = walk.routePoints.Count;
        return walk;
    }
    private static void Retime(Walk walk)
    {
        for (int i = 0; i < walk.routePoints.Count; i++)
        { walk.routePoints[i].gpsTimestamp = 1800000000 + i * 100; walk.routePoints[i].secondsSinceSessionStart = i * 100; walk.routePoints[i].startsNewSegment = i == 0; }
    }
}
