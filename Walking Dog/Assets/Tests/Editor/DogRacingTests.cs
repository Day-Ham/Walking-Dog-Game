using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WalkingDog.Racing;

public sealed class DogRacingTests
{
    [Test] public void TrainingCostsPointsOnlyImprovesChosenStatAndRespectsMaximum()
    {
        var baseline = new DogStats();
        Assert.Throws<InvalidOperationException>(() => baseline.Train(DogStat.Speed,19));
        var trained = baseline.Train(DogStat.Speed,20);
        Assert.That(trained.speed, Is.EqualTo(45)); Assert.That(trained.stamina, Is.EqualTo(40));
        Assert.That(trained.acceleration, Is.EqualTo(40)); Assert.That(trained.trainingCount, Is.EqualTo(1));
        Assert.That(baseline.speed, Is.EqualTo(40));
        trained.speed = 100; Assert.Throws<InvalidOperationException>(() => trained.Train(DogStat.Speed,100));
        Assert.Throws<ArgumentOutOfRangeException>(() => baseline.Train((DogStat)90,100));
    }
    [Test] public void SameSeedProducesSameResultsAcrossFrameRatesAndEntrantOrder()
    {
        var entrants = DogRaceSimulation.PracticeField(new DogStats());
        var fast = new DogRaceSimulation(entrants,18); var slow = new DogRaceSimulation(entrants.AsEnumerable().Reverse(),18);
        for (int i = 0; i < 6000 && !fast.Completed; i++) fast.Advance(1d/60);
        for (int i = 0; i < 1000 && !slow.Completed; i++) slow.Advance(.1);
        Assert.That(fast.Completed && slow.Completed, Is.True);
        CollectionAssert.AreEqual(fast.Standings().Select(r => r.Entry.Id), slow.Standings().Select(r => r.Entry.Id));
        foreach (var runner in fast.Runners)
            Assert.That(runner.FinishTime, Is.EqualTo(slow.Runners.Single(r => r.Entry.Id == runner.Entry.Id).FinishTime).Within(1e-8));
    }
    [TestCase(DogStat.Speed)] [TestCase(DogStat.Stamina)] [TestCase(DogStat.Acceleration)]
    public void EachStatImprovesItsDogOverTheSameRace(DogStat stat)
    {
        var basic = new DogStats(); var enhanced = basic.Copy();
        if (stat == DogStat.Speed) enhanced.speed = 80;
        if (stat == DogStat.Stamina) enhanced.stamina = 80;
        if (stat == DogStat.Acceleration) enhanced.acceleration = 80;
        var before = new DogRaceSimulation(DogRaceSimulation.PracticeField(basic),9,500);
        var after = new DogRaceSimulation(DogRaceSimulation.PracticeField(enhanced),9,500);
        for (int i=0; i<10; i++) { before.Advance(30); after.Advance(30); }
        Assert.That(after.Runners[0].FinishTime, Is.LessThan(before.Runners[0].FinishTime));
    }
    [Test] public void ActiveRaceKeepsItsStatSnapshotAndRanksOnlyActualFinishCrossings()
    {
        var stats = new DogStats(); var race = new DogRaceSimulation(DogRaceSimulation.PracticeField(stats),2,20);
        stats.speed = 100; Assert.That(race.Runners[0].Entry.Stats.speed, Is.EqualTo(40));
        race.Advance(30); Assert.That(race.Completed, Is.True);
        Assert.That(race.Standings().Select(r => r.FinishTime), Is.Ordered);
        Assert.That(race.Runners.All(r => r.Distance == 20 && r.FinishTime > 0), Is.True);
        var finish = race.Runners[0].FinishTime; race.Advance(1); Assert.That(race.Runners[0].FinishTime, Is.EqualTo(finish));
    }
}

public sealed class DogRacingSceneTests
{
    private DogGameScreens screens;
    private FakeStore store;
    private string owner;
    [SetUp] public void Setup()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        screens = GameObject.Find("Canvas").GetComponent<DogGameScreens>();
        Assert.That(screens, Is.Not.Null, "Connect the dog screens before scene tests.");
        store = new FakeStore(); owner = "alice";
        screens.OwnerReader = () => owner; screens.StoreFactory = () => Task.FromResult<IDogTrainingStore>(store);
    }
    [TearDown] public void Cleanup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    [Test] public async Task ScreensSharePaidStatsAndStartAnUncontrolledRace()
    {
        screens.OpenTraining(); await screens.RefreshAsync();
        Assert.That(Field<GameObject>("trainingScreen").activeSelf, Is.True);
        Assert.That(Field<GameObject>("raceScreen").activeSelf, Is.False);
        await screens.TrainAsync(DogStat.Speed);
        Assert.That(screens.Snapshot.speed, Is.EqualTo(45)); Assert.That(store.Points, Is.EqualTo(80));
        screens.OpenRace(); await screens.RefreshAsync(); screens.StartRace();
        Assert.That(screens.Race.Runners.Count, Is.EqualTo(4)); Assert.That(screens.Race.Runners[0].Entry.Stats.speed, Is.EqualTo(45));
        Assert.That(screens.Race.Runners[1].Entry.Name, Is.EqualTo("Dash"));
        screens.Close(); Assert.That(Field<GameObject>("raceScreen").activeSelf, Is.False); Assert.That(screens.Race, Is.Null);
    }
    [Test] public async Task InsufficientPointsNeverChangesStatsAndAccountSwitchRejectsLateTraining()
    {
        screens.OpenTraining(); await screens.RefreshAsync(); store.Points = 0;
        await screens.TrainAsync(DogStat.Stamina);
        Assert.That(screens.Snapshot.stamina, Is.EqualTo(40)); Assert.That(screens.TrainingStatus, Does.Contain("20 points"));
        store.Points = 100; store.Pending = new TaskCompletionSource<DogStats>();
        var request = screens.TrainAsync(DogStat.Speed); owner = "bob";
        screens.Close(); screens.OpenTraining(); await screens.RefreshAsync();
        store.Pending.SetResult(new DogStats { speed = 90 }); await request;
        Assert.That(screens.Snapshot.speed, Is.EqualTo(40));
    }
    [Test] public void MenuButtonsUseTheSavedScreenControllerAndStatsHaveThreeTrainingChoices()
    {
        var buttons = screens.GetComponentsInChildren<Button>(true).Where(b => b.name == "Race Button" || b.name == "Training Button").ToList();
        Assert.That(buttons.Count, Is.EqualTo(2));
        Assert.That(buttons.All(b => b.onClick.GetPersistentTarget(0) == screens), Is.True);
        Assert.That(Field<DogGameScreens.StatRow[]>("stats").Select(r => r.stat).Distinct().Count(), Is.EqualTo(3));
        Assert.That(Field<DogGameScreens.RaceLane[]>("lanes").Length, Is.EqualTo(4));
        Assert.That(screens.GetComponentsInChildren<DogIconGraphic>(true).All(icon => icon.GetComponent<CanvasRenderer>() != null), Is.True);
    }
    private T Field<T>(string field) => (T)typeof(DogGameScreens).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(screens);
    private sealed class FakeStore : IDogTrainingStore
    {
        public long Points = 100; public TaskCompletionSource<DogStats> Pending;
        private readonly Dictionary<string,DogStats> dogs = new Dictionary<string,DogStats>();
        public Task<DogStats> LoadAsync(string uid,CancellationToken token)
        { if (!dogs.ContainsKey(uid)) dogs[uid] = new DogStats(); return Task.FromResult(dogs[uid].Copy()); }
        public Task<DogStats> TrainAsync(string uid,DogStat stat,string receipt,CancellationToken token)
        {
            if (Pending != null) return Pending.Task;
            var next = dogs[uid].Train(stat,Points); Points -= 20; dogs[uid] = next;
            return Task.FromResult(next.Copy());
        }
    }
}
