using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class ContinuousStepsTests
{
    private string directory;
    [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "ContinuousStepsTests", Guid.NewGuid().ToString("N")); }
    [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test] public void StepsSurviveRestartAndSensorCorrectionsDoNotPayTwice()
    {
        var steps = new ContinuousSteps(directory);
        steps.Observe("alice", 105);
        steps.Observe("alice", 100);
        steps.Observe("alice", 105);
        Assert.That(steps.Get("alice").total, Is.EqualTo(105));
        var stream = steps.Get("alice").streamId;
        steps = new ContinuousSteps(directory);
        steps.Observe("alice", 208);
        steps.Observe("alice", 255);
        Assert.That(steps.Get("alice").total, Is.EqualTo(360));
        Assert.That(steps.Get("alice").streamId, Is.EqualTo(stream));
    }

    [Test] public void AccountsAndGuestStepsStaySeparate()
    {
        var steps = new ContinuousSteps(directory);
        steps.Observe("", 30);
        steps.Observe("alice", 35);
        steps.Observe("alice", 45);
        steps.Observe("bob", 45);
        steps.Observe("bob", 60);
        steps.Observe("alice", 60);
        steps.Observe("alice", 65);
        Assert.That(steps.Get("").total, Is.EqualTo(30));
        Assert.That(steps.Get("alice").total, Is.EqualTo(15));
        Assert.That(steps.Get("bob").total, Is.EqualTo(15));
    }

    [Test] public void LateAcknowledgementDoesNotDiscardNewSteps()
    {
        var steps = new ContinuousSteps(directory);
        steps.Observe("alice", 100);
        var upload = steps.Snapshot("alice");
        steps.Observe("alice", 120);
        steps.Acknowledge(upload);
        var restarted = new ContinuousSteps(directory).Get("alice");
        Assert.That(restarted.total, Is.EqualTo(120));
        Assert.That(restarted.acknowledged, Is.EqualTo(100));
    }

    [Test] public void BackupRecoveryRetainsIdentityAndCorruptionNeverStartsNewStream()
    {
        var steps = new ContinuousSteps(directory);
        steps.Observe("alice", 100);
        steps.Observe("alice", 120);
        var id = steps.Get("alice").streamId;
        var path = Directory.GetFiles(directory, "*.json")[0];
        File.WriteAllText(path, "broken");
        Assert.That(new ContinuousSteps(directory).Get("alice").streamId, Is.EqualTo(id));
        File.WriteAllText(path + ".bak", "broken");
        Assert.Throws<IOException>(() => new ContinuousSteps(directory).Get("alice"));
    }

    [Test] public void TerritoryStartAndStopDoNotInterruptContinuousTotal()
    {
        var obj = new GameObject("continuous steps test");
        try
        {
            var manager = obj.AddComponent<StepCountAndGpsManager>();
            typeof(StepCountAndGpsManager).GetField("continuousSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new ContinuousSteps(Path.Combine(directory, "steps")));
            typeof(StepCountAndGpsManager).GetField("localWalks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new LocalWalkRepository(Path.Combine(directory, "walks")));
            manager.SetStep(105);
            manager.BeginWalkingSession();
            manager.SetStep(313);
            manager.EndWalkingSession();
            manager.SetStep(360);
            Assert.That(manager.TotalTrackedSteps, Is.EqualTo(360));
            Assert.That(manager.WalkingSessionSteps, Is.EqualTo(208));
            var walk = manager.LoadSavedWalks()[0];
            Assert.That(walk.stepAccountingVersion, Is.EqualTo(1));
            Assert.That(WalkSummary.FromWalk(walk).stepAccountingVersion, Is.EqualTo(1));
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }

    [Test] public void LegacyRecoveryStepsRemainOnLegacyRewardPath()
    {
        var steps = new ContinuousSteps(directory);
        steps.Observe("alice", 20);
        steps.Observe("alice", 100, legacyWalk: true);
        steps.Observe("alice", 110);
        Assert.That(steps.Get("alice").total, Is.EqualTo(30));
    }
}
