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
using UnityEngine.TestTools;
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
    [Test] public void RoomCodesAreCleanNormalizedAndFormatted()
    {
        var code = RaceRoomCodes.Create(5);
        Assert.That(code.Length, Is.EqualTo(5));
        Assert.That(RaceRoomCodes.Normalize("  k9-742  "), Is.EqualTo("K9742"));
        Assert.That(RaceRoomCodes.Format("k9742"), Is.EqualTo("K9-742"));
        Assert.That(RaceRoomCodes.Normalize("invalid!code?"), Is.EqualTo(""));
    }
    [Test] public void RaceRoomSerializesAndBuildsMultiplayerFieldFillingAiSlots()
    {
        var hostStats = new DogStats { speed = 60, stamina = 55, acceleration = 50 };
        var entrant1 = new RaceEntrant("host1", "Alice", hostStats, "collar_gold", isHost: true);
        var entrant2 = new RaceEntrant("guest2", "Bob", new DogStats { speed = 45, stamina = 45, acceleration = 45 }, "collar_blue", isHost: false);
        var room = new RaceRoom("K9742", "host1", "Alice", "waiting", 0, 200, new[] { entrant1, entrant2 });

        var dict = room.ToDictionary();
        var parsed = RaceRoom.Parse("K9742", dict);
        Assert.That(parsed.RoomCode, Is.EqualTo("K9742"));
        Assert.That(parsed.Entrants.Count, Is.EqualTo(2));
        Assert.That(parsed.Entrants[0].DisplayName, Is.EqualTo("Alice"));
        Assert.That(parsed.Entrants[0].EquippedItemId, Is.EqualTo("collar_gold"));
        Assert.That(parsed.CanStart("host1"), Is.True);
        Assert.That(parsed.CanStart("guest2"), Is.False);

        var field = parsed.BuildSimulationField();
        Assert.That(field.Count, Is.EqualTo(4));
        Assert.That(field[0].Name, Is.EqualTo("Alice"));
        Assert.That(field[0].EquippedItemId, Is.EqualTo("collar_gold"));
        Assert.That(field[1].Name, Is.EqualTo("Bob"));
        Assert.That(field[2].Name, Is.EqualTo("Dash"));
        Assert.That(field[3].Name, Is.EqualTo("Pip"));
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
        var menu = screens.GetComponentsInChildren<Transform>(true).Single(t => t.name == "SideArea").Find("Buttons");
        var buttons = menu.GetComponentsInChildren<Button>(true).Where(b => b.name == "Race Button" || b.name == "Training Button").ToList();
        Assert.That(buttons.Count, Is.EqualTo(2));
        Assert.That(buttons.All(b => b.onClick.GetPersistentTarget(0) == screens), Is.True);
        Assert.That(Field<DogGameScreens.StatRow[]>("stats").Select(r => r.stat).Distinct().Count(), Is.EqualTo(3));
        Assert.That(Field<DogGameScreens.RaceLane[]>("lanes").Length, Is.EqualTo(4));
        Assert.That(screens.GetComponentsInChildren<DogIconGraphic>(true).All(icon => icon.GetComponent<CanvasRenderer>() != null), Is.True);
    }
    [Test] public async Task LobbyScreenCanCreateRoomAndTransitionsOnRaceStart()
    {
        var fakeLobby = new FakeRaceLobbyService();
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(fakeLobby);
        screens.OpenLobby();
        await screens.RefreshAsync();
        Assert.That(Field<GameObject>("lobbyScreen").activeSelf, Is.True);

        await screens.CreateRoomAsync();
        Assert.That(screens.CurrentRoom, Is.Not.Null);
        Assert.That(screens.CurrentRoom.Entrants.Count, Is.EqualTo(1));

        // Simulate a second player joining
        await fakeLobby.JoinRoomAsync(screens.CurrentRoom.RoomCode, "bob", "Bob", new DogStats(), "item_red", CancellationToken.None);
        fakeLobby.TriggerUpdate();
        Assert.That(screens.CurrentRoom.Entrants.Count, Is.EqualTo(2));

        // Start race
        await screens.StartLobbyRaceAsync();
        Assert.That(screens.IsBusy, Is.False, "A successful host start must release the controls.");
        fakeLobby.TriggerUpdate();
        Assert.That(screens.Race, Is.Not.Null);
        Assert.That(screens.Race.Runners.Count, Is.EqualTo(4));
        Assert.That(Field<GameObject>("raceScreen").activeSelf, Is.True);
        screens.Race.Advance(60);
        screens.RenderRace();
        Assert.That(Field<Button>("startRace").interactable, Is.True);
        screens.Close();
    }

    [Test] public async Task CreateRoomFailureStaysVisibleAndButtonsRecover()
    {
        var lobby = new FakeRaceLobbyService { CreateError = new InvalidOperationException("Room access was denied.") };
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(lobby);
        screens.OpenLobby(); await screens.RefreshAsync();
        LogAssert.Expect(LogType.Warning, "Race room request failed: InvalidOperationException");
        await screens.CreateRoomAsync();
        screens.RenderLobby();
        Assert.That(screens.LobbyStatus, Does.Contain("access was denied"));
        Assert.That(screens.CurrentRoom, Is.Null);
        Assert.That(screens.IsBusy, Is.False);
        Assert.That(Field<Button>("createRoomButton").interactable, Is.True);
    }

    [Test] public async Task TimedOutCreationRecoversAndLateSuccessCannotReopenTheRoom()
    {
        var lobby = new FakeRaceLobbyService { PendingCreate = new TaskCompletionSource<RaceRoom>() };
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(lobby);
        screens.LobbyTimeout = TimeSpan.FromMilliseconds(15);
        screens.OpenLobby(); await screens.RefreshAsync();
        await screens.CreateRoomAsync();
        Assert.That(screens.LobbyStatus, Does.Contain("timed out"));
        Assert.That(screens.IsBusy, Is.False);
        Assert.That(Field<Button>("createRoomButton").interactable, Is.True);
        lobby.PendingCreate.SetResult(lobby.Room);
        await Task.Yield();
        Assert.That(screens.CurrentRoom, Is.Null);
        Assert.That(lobby.Leaves, Is.EqualTo(1));
    }

    [Test] public async Task ClosingDuringCreateAndSwitchingAccountsDiscardLateResults()
    {
        var lobby = new FakeRaceLobbyService { PendingCreate = new TaskCompletionSource<RaceRoom>() };
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(lobby);
        screens.OpenLobby(); await screens.RefreshAsync();
        var creating = screens.CreateRoomAsync();
        screens.Close(); owner = "bob"; screens.OpenLobby(); await screens.RefreshAsync();
        lobby.PendingCreate.SetResult(lobby.Room);
        await creating; await Task.Yield();
        Assert.That(screens.CurrentRoom, Is.Null);
        Assert.That(screens.Snapshot.speed, Is.EqualTo(40));
        Assert.That(lobby.Subscriptions, Is.Zero);
        Assert.That(screens.IsBusy, Is.False);
    }

    [Test] public async Task ClosedAndDestroyedScreensIgnoreQueuedRoomCallbacks()
    {
        var lobby = new FakeRaceLobbyService();
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(lobby);
        screens.OpenLobby(); await screens.RefreshAsync(); await screens.CreateRoomAsync();
        var racing = new RaceRoom("K9742", "alice", "Alice", "racing", 91, 200, lobby.Room.Entrants);
        screens.Close(); lobby.Trigger(racing);
        Assert.That(Field<GameObject>("raceScreen").activeSelf, Is.False);
        Assert.That(screens.CurrentRoom, Is.Null);
        Assert.That(lobby.Disposals, Is.EqualTo(1));
        screens.OpenLobby(); await screens.RefreshAsync(); await screens.CreateRoomAsync();
        // EditMode does not run the normal Play-mode destruction lifecycle.
        typeof(DogGameScreens).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screens, null);
        UnityEngine.Object.DestroyImmediate(screens);
        Assert.That(lobby.Disposals, Is.EqualTo(2));
        Assert.DoesNotThrow(() => lobby.Trigger(racing));
    }

    [Test] public async Task MissingAccountAndInvalidRoomCodeExplainTheProblemWithoutSendingRequests()
    {
        var lobby = new FakeRaceLobbyService();
        screens.LobbyServiceFactory = () => Task.FromResult<IRaceLobbyService>(lobby);
        owner = ""; screens.OpenLobby(); await screens.RefreshAsync(); await screens.CreateRoomAsync();
        Assert.That(screens.LobbyStatus, Does.Contain("Sign in"));
        Assert.That(Field<Button>("createRoomButton").interactable, Is.False);
        owner = "alice"; screens.Close(); screens.OpenLobby(); await screens.RefreshAsync();
        Field<TMPro.TMP_InputField>("roomCodeInput").text = "ABC";
        await screens.JoinRoomAsync();
        Assert.That(screens.LobbyStatus, Does.Contain("five-character"));
        Assert.That(lobby.Subscriptions, Is.Zero);
    }
    private sealed class FakeRaceLobbyService : IRaceLobbyService
    {
        public RaceRoom Room;
        public Exception CreateError;
        public TaskCompletionSource<RaceRoom> PendingCreate;
        public int Leaves, Subscriptions, Disposals;
        private Action<RaceRoom> onUpdate;

        public Task<RaceRoom> CreateRoomAsync(string hostUid, string hostName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            var host = new RaceEntrant(hostUid, hostName, stats, equippedItemId, isHost: true);
            Room = new RaceRoom("K9742", hostUid, hostName, "waiting", 0, 200, new[] { host });
            if (CreateError != null) return Task.FromException<RaceRoom>(CreateError);
            return PendingCreate != null ? PendingCreate.Task : Task.FromResult(Room);
        }
        public Task<RaceRoom> JoinRoomAsync(string roomCode, string playerUid, string playerName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            var list = Room.Entrants.ToList();
            list.Add(new RaceEntrant(playerUid, playerName, stats, equippedItemId, isHost: false));
            Room = new RaceRoom(roomCode, Room.HostUid, Room.HostName, Room.Status, Room.Seed, Room.TrackLength, list);
            return Task.FromResult(Room);
        }
        public Task LeaveRoomAsync(string roomCode, string playerUid, CancellationToken token)
        {
            Leaves++;
            var list = Room.HostUid == playerUid ? Room.Entrants.ToList() : Room.Entrants.Where(e => e.PlayerId != playerUid).ToList();
            Room = new RaceRoom(roomCode, Room.HostUid, Room.HostName, Room.HostUid == playerUid ? "closed" : Room.Status, Room.Seed, Room.TrackLength, list);
            return Task.CompletedTask;
        }
        public Task StartRaceAsync(string roomCode, string hostUid, int seed, CancellationToken token)
        {
            Room = new RaceRoom(roomCode, Room.HostUid, Room.HostName, "racing", seed, Room.TrackLength, Room.Entrants);
            return Task.CompletedTask;
        }
        public IDisposable ListenRoom(string roomCode, Action<RaceRoom> onUpdate, Action<Exception> onError)
        {
            this.onUpdate = onUpdate;
            Subscriptions++;
            return new DummySub(() => Disposals++);
        }
        public void TriggerUpdate() => onUpdate?.Invoke(Room);
        public void Trigger(RaceRoom room) => onUpdate?.Invoke(room); // Deliberately model an already queued callback after Dispose.
        private sealed class DummySub : IDisposable
        {
            private Action disposed;
            public DummySub(Action disposed) { this.disposed = disposed; }
            public void Dispose() { var action = disposed; disposed = null; action?.Invoke(); }
        }
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
