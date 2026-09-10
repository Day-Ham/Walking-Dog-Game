using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class WalkTrackingTests
{
    private string directory;
    private GameObject gameObject;
    private StepCountAndGpsManager manager;
    private const double Epoch = 1800000000;

    [SetUp] public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "WalkingDogTrackingTests", Guid.NewGuid().ToString("N"));
    }
    [TearDown] public void Cleanup()
    {
        if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Test] public void RejectsNonfiniteStaleFutureAndInaccurateFixes()
    {
        var filter = new WalkGpsFilter();
        Assert.That(filter.Accept(double.NaN, 121, 5, Epoch, Epoch, out _), Is.False);
        Assert.That(filter.Accept(14.5, double.PositiveInfinity, 5, Epoch, Epoch, out _), Is.False);
        Assert.That(filter.Accept(14.5, 121, double.NaN, Epoch, Epoch, out _), Is.False);
        Assert.That(filter.Accept(14.5, 121, 5, Epoch, Epoch + 11, out _), Is.False);
        Assert.That(filter.Accept(14.5, 121, 5, Epoch + 3, Epoch, out _), Is.False);
        Assert.That(filter.Accept(14.5, 121, 25, Epoch, Epoch, out _), Is.False);
        Assert.That(filter.Accept(0, 0, 5, Epoch, Epoch, out var start), Is.True);
        Assert.That(start, Is.True);
    }

    [Test] public void JumpBelowOldHundredMetreFloorIsRejectedAndBreaksRoute()
    {
        var filter = new WalkGpsFilter();
        filter.Accept(14.5, 121, 5, Epoch, Epoch, out _);
        Assert.That(filter.Accept(14.5006, 121, 5, Epoch + 1.5, Epoch + 1.5, out _), Is.False);
        Assert.That(filter.HasGaps, Is.True);
        Assert.That(filter.Accept(14.5006, 121, 5, Epoch + 3, Epoch + 3, out var start), Is.True);
        Assert.That(start, Is.True);
    }

    [Test] public void OutageAndExplicitPauseStartNewSegments()
    {
        var filter = new WalkGpsFilter();
        filter.Accept(14.5, 121, 5, Epoch, Epoch, out _);
        Assert.That(filter.Accept(14.501, 121, 5, Epoch + 30, Epoch + 30, out var start), Is.True);
        Assert.That(start, Is.True);
        filter.Interrupt("Paused");
        Assert.That(filter.Accept(14.5011, 121, 5, Epoch + 32, Epoch + 32, out start), Is.True);
        Assert.That(start, Is.True);
        Assert.That(filter.HasGaps, Is.True);
    }

    [Test] public void FreshStationaryFixesDoNotCreateDistanceOrFalseOutages()
    {
        var filter = new WalkGpsFilter();
        filter.Accept(14.5, 121, 5, Epoch, Epoch, out _);
        for (var i = 1; i <= 60; i++)
            Assert.That(filter.Accept(14.500005, 121, 5, Epoch + i, Epoch + i, out _), Is.False);
        Assert.That(filter.Accept(14.50005, 121, 5, Epoch + 62, Epoch + 62, out var start), Is.True);
        Assert.That(start, Is.False);
        Assert.That(filter.HasGaps, Is.False);
    }

    [Test] public void DuplicateAndOutOfOrderFixesAreIgnored()
    {
        var filter = new WalkGpsFilter();
        filter.Accept(14.5, 121, 5, Epoch, Epoch, out _);
        Assert.That(filter.Accept(14.6, 121, 5, Epoch, Epoch, out _), Is.False);
        Assert.That(filter.Accept(14.6, 121, 5, Epoch - 1, Epoch, out _), Is.False);
        Assert.That(filter.HasGaps, Is.False);
    }

    [Test] public void ManagerDoesNotCountDistanceAcrossMissingTracking()
    {
        CreateManager();
        var now = StepCountAndGpsManager.UtcSeconds;
        manager.SetGpsLocation(14.5f, 121, 5, now - 5, now - 5);
        manager.BeginWalkingSession();
        manager.InterruptTracking("GPS lost");
        manager.SetGpsLocation(14.51f, 121, 5, now, now);
        Assert.That(manager.RoutePointCount, Is.EqualTo(2));
        Assert.That(manager.WalkingSessionDistanceMeters, Is.Zero);
        Assert.That(manager.RoutePointSamples[1].startsNewSegment, Is.True);
        manager.EndWalkingSession();
        Assert.That(manager.LoadSavedWalks()[0].hasTrackingGaps, Is.True);
    }

    [Test] public void OldCachedFixCannotAnchorNewWalk()
    {
        CreateManager();
        var old = StepCountAndGpsManager.UtcSeconds - 30;
        manager.SetGpsLocation(14.5f, 121, 5, old, old);
        Assert.That(manager.HasFreshLocation, Is.False);
        manager.BeginWalkingSession();
        Assert.That(manager.RoutePointCount, Is.Zero);
    }

    [Test] public void ActiveWalkRecoversWithSameIdStepsAndSegmentBreak()
    {
        CreateManager();
        manager.SetStep(100);
        manager.SetGpsLocation(14.5f, 121, 5);
        manager.BeginWalkingSession();
        var id = manager.CurrentSessionId;
        manager.SetStep(130);
        manager.SaveCheckpoint();
        Assert.That(manager.LoadSavedWalks(), Is.Empty, "Drafts must never appear as completed walks.");
        UnityEngine.Object.DestroyImmediate(gameObject);
        CreateManager();
        manager.SetStep(2); // Device/app step baseline changed across the restart.
        Assert.That(manager.RecoverableWalk.id, Is.EqualTo(id));
        Assert.That(manager.RecoverWalk(true), Is.True);
        manager.SetStep(12);
        manager.SetGpsLocation(14.51f, 121, 5);
        Assert.That(manager.CurrentSessionId, Is.EqualTo(id));
        Assert.That(manager.WalkingSessionSteps, Is.EqualTo(40));
        Assert.That(manager.WalkingSessionDistanceMeters, Is.Zero);
        Assert.That(manager.RoutePointSamples[1].startsNewSegment, Is.True);
        manager.EndWalkingSession();
        manager.SaveCurrentWalkingSession();
        Assert.That(manager.LoadSavedWalks().Count, Is.EqualTo(1));
        Assert.That(manager.RecoverableWalk, Is.Null);
    }

    [Test] public void RecoveryDoesNotExposeOtherAccountsAndDoesNotReplacePendingWalk()
    {
        CreateManager();
        manager.BeginWalkingSession();
        var id = manager.CurrentSessionId;
        UnityEngine.Object.DestroyImmediate(gameObject);
        CreateManager();
        manager.BeginWalkingSession();
        Assert.That(manager.IsWalkingSessionActive, Is.False);
        Assert.That(manager.RecoverableWalk.id, Is.EqualTo(id));
        var drafts = new LocalWalkRepository(Path.Combine(directory, "Active"));
        drafts.AssignOwner(id, "another-user");
        Assert.That(manager.RecoverableWalk, Is.Null);
        Assert.That(drafts.LoadAll().Count, Is.EqualTo(1));
    }

    [Test] public void CheckpointBackupRecoversAndCompletedCopyWinsAfterInterruptedCleanup()
    {
        CreateManager();
        manager.BeginWalkingSession();
        manager.SetStep(5);
        manager.SaveCheckpoint();
        var id = manager.CurrentSessionId;
        UnityEngine.Object.DestroyImmediate(gameObject);
        var drafts = new LocalWalkRepository(Path.Combine(directory, "Active"));
        var path = drafts.GetFilePaths()[0];
        File.WriteAllText(path, "truncated");
        CreateManager();
        Assert.That(manager.RecoverWalk(false), Is.True);
        var complete = manager.LocalWalks.Find(id);
        Assert.That(complete, Is.Not.Null);
        drafts.SaveCheckpoint(complete); // Simulate crash after final commit but before draft deletion.
        Assert.That(manager.RecoverableWalk, Is.Null);
    }

    [Test] public void NativeBacklogPreservesSampleTimesAndIgnoresReplayedSequence()
    {
        CreateManager();
        manager.BeginWalkingSession();
        var now = StepCountAndGpsManager.UtcSeconds;
        typeof(StepCountAndGpsManager).GetField("routeEpoch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, now - 120);
        typeof(StepCountAndGpsManager).GetField("sessionStartTime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(manager, Time.realtimeSinceStartup - 120);
        var first = new AndroidWalkTracking.Sample { sequence = 1, latitude = 14.5f, longitude = 121, accuracy = 5, timestamp = now - 100, observedAt = now - 100 };
        var second = new AndroidWalkTracking.Sample { sequence = 2, latitude = 14.50005f, longitude = 121, accuracy = 5, timestamp = now - 95, observedAt = now - 95 };
        manager.ApplyNativeSample(first);
        manager.ApplyNativeSample(second);
        manager.ApplyNativeSample(second);
        Assert.That(manager.RoutePointCount, Is.EqualTo(2));
        Assert.That(manager.RoutePointSamples[0].secondsSinceSessionStart, Is.EqualTo(20).Within(0.1));
        Assert.That(manager.RoutePointSamples[1].secondsSinceSessionStart, Is.EqualTo(25).Within(0.1));
        Assert.That(manager.RoutePointSamples[1].startsNewSegment, Is.False);
        Assert.That(manager.WalkingSessionDistanceMeters, Is.InRange(4f, 7f));
        manager.SaveCheckpoint();
        Assert.That(manager.RecoverableWalk.nativeSequence, Is.EqualTo(2));
    }

    private void CreateManager()
    {
        gameObject = new GameObject("Walk tracking test");
        manager = gameObject.AddComponent<StepCountAndGpsManager>();
        typeof(StepCountAndGpsManager).GetField("localWalks", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(manager, new LocalWalkRepository(directory, _ => { }));
    }
}
