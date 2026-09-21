using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using WalkingDog.Leaderboards;

public sealed class LeaderboardSceneTests
{
    private LeaderboardPanelUI panel;
    private FakeService service;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        panel = GameObject.Find("Canvas").transform.Find("Leaderboard Screen").GetComponent<LeaderboardPanelUI>();
        Assert.That(panel, Is.Not.Null, "Run LeaderboardSceneSetup.Connect before these integration tests.");
        service = new FakeService();
        panel.ServiceFactory = () => Task.FromResult<ILeaderboardService>(service);
        panel.gameObject.SetActive(true);
        Invoke("Awake"); // Editor tests exercise the same callbacks without Play mode or Firebase.
    }

    [TearDown]
    public void TearDown()
    {
        if (panel != null) { Invoke("OnDisable"); Invoke("OnDestroy"); }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [Test]
    public async Task FriendsTabUsesSeparateScopeAndLateGlobalResultsAreDiscarded()
    {
        var old = new TaskCompletionSource<LeaderboardSnapshot>();
        service.Read = (_, __) => old.Task;
        var pending = panel.RefreshAsync();
        service.Read = (metric, _) => Task.FromResult(new LeaderboardSnapshot(metric, "me", new LeaderboardEntry[0], null, LeaderboardScope.Friends));
        panel.ShowFriends();
        Assert.That(service.LastScope, Is.EqualTo(LeaderboardScope.Friends));
        old.SetResult(FakeService.Data(LeaderboardMetric.Distance));
        await pending;
        Assert.That(panel.VisibleCardCount, Is.Zero);
        Assert.That(panel.StatusText, Does.Contain("Add friends"));
        panel.ShowGlobal();
        Assert.That(service.LastScope, Is.EqualTo(LeaderboardScope.Global));
    }

    [Test]
    public async Task ExistingButtonsOpenCloseAndRefreshConnectedScreen()
    {
        panel.gameObject.SetActive(false);
        var launch = GameObject.Find("Leaderboard Button").GetComponent<Button>();
        launch.onClick.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
        launch.onClick.Invoke();
        Assert.That(panel.gameObject.activeSelf, Is.True);
        await panel.RefreshAsync();
        Assert.That(panel.VisibleCardCount, Is.EqualTo(2));
        var refresh = Field<Button>("refresh");
        refresh.onClick.Invoke();
        Assert.That(service.Reads, Is.EqualTo(2));
        var back = panel.transform.Find("LeaderBG/LeaderSafeArea/Back").GetComponent<Button>();
        back.onClick.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
        back.onClick.Invoke();
        Assert.That(panel.gameObject.activeSelf, Is.False);
    }

    [Test]
    public async Task DisplaysScoresAndPersonalTotalsAndKeepsTemplateHidden()
    {
        await panel.RefreshAsync();
        Assert.That(Field<LeaderboardCardUI>("cardTemplate").gameObject.activeSelf, Is.False);
        Assert.That(Field<TMP_Text>("personalTotals").text, Does.Contain("1.23 km"));
        var content = Field<RectTransform>("content");
        Assert.That(content.Find("Player 2").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("My Walker"));
        var details = content.Find("Player 2/Score details").GetComponent<TMP_Text>();
        Assert.That(details.text, Does.Contain("#2 · YOU"));
        Field<Button>("steps").onClick.Invoke();
        Assert.That(service.LastMetric, Is.EqualTo(LeaderboardMetric.Steps));
        Assert.That(panel.StatusText, Does.Contain("Steps"));
    }

    [Test]
    public async Task LateResultsCannotReplaceNewerMetricSelection()
    {
        var old = new TaskCompletionSource<LeaderboardSnapshot>();
        service.Read = (_, __) => old.Task;
        var first = panel.RefreshAsync();
        service.Read = (metric, _) => Task.FromResult(FakeService.Data(metric));
        panel.ShowSteps();
        old.SetResult(FakeService.Data(LeaderboardMetric.Distance));
        await first;
        Assert.That(panel.StatusText, Does.Contain("Steps"));
        Assert.That(panel.VisibleCardCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ClosedPanelDiscardsLateResultsAndClearsIdentity()
    {
        var read = new TaskCompletionSource<LeaderboardSnapshot>();
        service.Read = (_, __) => read.Task;
        var task = panel.RefreshAsync();
        Invoke("OnDisable");
        panel.gameObject.SetActive(false);
        read.SetResult(FakeService.Data(LeaderboardMetric.Distance));
        await task;
        Assert.That(panel.VisibleCardCount, Is.Zero);
        Assert.That(Field<TMP_Text>("personalTotals").text, Is.Empty);
        Assert.That(Field<TMP_InputField>("nickname").text, Is.Empty);
    }

    [Test]
    public async Task FailureAndSignedOutStatesDoNotPretendToBeZeroScores()
    {
        service.Read = (_, __) => Task.FromException<LeaderboardSnapshot>(new TimeoutException());
        await panel.RefreshAsync();
        Assert.That(panel.StatusText, Does.Contain("Couldn't load"));
        Assert.That(Field<Button>("refresh").interactable, Is.True);
        service.AuthenticatedUserId = "";
        await panel.RefreshAsync();
        Assert.That(panel.StatusText, Does.Contain("Sign in"));
        Assert.That(panel.VisibleCardCount, Is.Zero);
        Assert.That(Field<Button>("saveNickname").interactable, Is.False);
    }

    private T Field<T>(string name) => (T)typeof(LeaderboardPanelUI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
    private void Invoke(string name) => typeof(LeaderboardPanelUI).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);

    [Test]
    public async Task FriendsManagerShowsRequestsAndClearsIdentityOnClose()
    {
        await panel.RefreshAsync();
        var safe = panel.transform.Find("LeaderBG/LeaderSafeArea");
        safe.Find("Manage friends").GetComponent<Button>().onClick.Invoke();
        var manager = safe.Find("Friends manager").GetComponent<FriendsPanelUI>();
        var run = typeof(FriendsPanelUI).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        await (Task)run.Invoke(manager, new object[] { null, FriendAction.Send });
        Assert.That(manager.transform.Find("Friend code").GetComponent<TMP_Text>().text, Is.EqualTo("me"));
        Assert.That(manager.transform.Find("Friends list/Friend rows").childCount, Is.EqualTo(3));
        manager.transform.Find("Friends list/Friend rows/Friend incoming/Accept").GetComponent<Button>().onClick.Invoke();
        Assert.That(service.LastFriendAction, Is.EqualTo(FriendAction.Accept));
        Assert.That(service.LastFriendCode, Is.EqualTo("incoming"));
        manager.transform.Find("Close friends").GetComponent<Button>().onClick.Invoke();
        typeof(FriendsPanelUI).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
        Assert.That(manager.gameObject.activeSelf, Is.False);
        Assert.That(manager.transform.Find("Friend code").GetComponent<TMP_Text>().text, Is.Empty);
    }

    [Test]
    public async Task FriendsManagerRejectsLateResultsAfterClosing()
    {
        await panel.RefreshAsync();
        var completion = new TaskCompletionSource<IReadOnlyList<FriendEntry>>();
        service.FriendRead = completion.Task;
        var safe = panel.transform.Find("LeaderBG/LeaderSafeArea");
        safe.Find("Manage friends").GetComponent<Button>().onClick.Invoke();
        var manager = safe.Find("Friends manager").GetComponent<FriendsPanelUI>();
        var task = (Task)typeof(FriendsPanelUI).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new object[] { null, FriendAction.Send });
        manager.gameObject.SetActive(false);
        completion.SetResult(new[] { new FriendEntry("other", "Other Walker", "other", true) });
        await task;
        Assert.That(manager.transform.Find("Friend code").GetComponent<TMP_Text>().text, Is.Empty);
        Assert.That(manager.transform.Find("Friends list/Friend rows").childCount, Is.Zero);
    }

    private sealed class FakeService : ILeaderboardService, IFriendsService
    {
        public string AuthenticatedUserId { get; set; } = "me";
        public int Reads;
        public LeaderboardMetric LastMetric;
        public Func<LeaderboardMetric, CancellationToken, Task<LeaderboardSnapshot>> Read = (metric, _) => Task.FromResult(Data(metric));
        public Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, CancellationToken cancellationToken)
        { Reads++; LastMetric = metric; return Read(metric, cancellationToken); }
        public Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, LeaderboardScope scope, CancellationToken cancellationToken)
        { LastScope = scope; return LoadAsync(metric, cancellationToken); }
        public LeaderboardScope LastScope;
        public FriendAction LastFriendAction;
        public string LastFriendCode;
        public Task<IReadOnlyList<FriendEntry>> FriendRead;
        public Task<IReadOnlyList<FriendEntry>> LoadFriendsAsync(CancellationToken token) => FriendRead ?? Task.FromResult<IReadOnlyList<FriendEntry>>(new[] {
            new FriendEntry("incoming", "Incoming Walker", "incoming", false),
            new FriendEntry("outgoing", "Outgoing Walker", "me", false),
            new FriendEntry("accepted", "Accepted Walker", "accepted", true)
        });
        public Task ChangeFriendAsync(string code, FriendAction action, CancellationToken token)
        { LastFriendCode = code; LastFriendAction = action; return Task.CompletedTask; }
        public Task SaveDisplayNameAsync(string name, CancellationToken token) => Task.CompletedTask;
        public void Dispose() { }
        public static LeaderboardSnapshot Data(LeaderboardMetric metric)
        {
            var own = new LeaderboardEntry("me", "My Walker", 1230, 2100, 2);
            return new LeaderboardSnapshot(metric, "me", new List<LeaderboardEntry> {
                new LeaderboardEntry("other", "Other Walker", 2340, 3300, 3), own
            }, own);
        }
    }
}
