using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WalkingDog.Leaderboards;

public sealed class SocialTests
{
    [Test]
    public void FeedOrdersActualWalkTimeAndDeduplicatesPerAuthor()
    {
        var alice = new SocialPlayer { Id = "alice" };
        var bob = new SocialPlayer { Id = "bob" };
        var earlier = new SocialActivity { Id = "same", Player = alice, EndedUtc = DateTime.UtcNow.AddDays(-1) };
        var later = new SocialActivity { Id = "same", Player = bob, EndedUtc = DateTime.UtcNow };
        var result = SocialFeed.Recent(new[] { earlier, later, earlier });
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.SameAs(later));
        Assert.That(SocialFeed.Recent(Enumerable.Range(0, 120).Select(i => new SocialActivity {
            Id = i.ToString(), Player = alice, EndedUtc = DateTime.UtcNow.AddMinutes(-i)
        })).Count, Is.EqualTo(100));
    }
}

public sealed class SocialSceneTests
{
    private SocialPanelUI panel;
    private FakeSocial service;
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        panel = GameObject.Find("Canvas").transform.Find("Profile Screen").GetComponent<SocialPanelUI>();
        Assert.That(panel, Is.Not.Null, "Run SocialSceneSetup.Connect before scene tests.");
        service = new FakeSocial(); panel.ServiceFactory = () => Task.FromResult<ISocialService>(service);
        panel.gameObject.SetActive(true); Invoke("Awake");
    }
    [TearDown]
    public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    private T Field<T>(string name) => (T)typeof(SocialPanelUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
    private void Invoke(string name) => typeof(SocialPanelUI).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);

    [Test]
    public async Task ExistingScreenShowsActivityAndDirectionalFollowButtons()
    {
        await panel.RefreshAsync();
        Assert.That(panel.VisibleCardCount, Is.EqualTo(1));
        Assert.That(Field<SocialCardUI>("template").gameObject.activeSelf, Is.False);
        Assert.That(Field<TMP_Text>("followingCount").text, Is.EqualTo("1 following"));
        Field<Button>("followers").onClick.Invoke();
        Assert.That(service.LastTab, Is.EqualTo(SocialTab.Followers));
        var row = Field<RectTransform>("content").Find("Social player other");
        var follow = row.Find("Follow User Button").GetComponent<Button>();
        Assert.That(follow.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Follow back"));
        follow.onClick.Invoke();
        Assert.That(service.Following, Is.True);
        row = Field<RectTransform>("content").Find("Social player other");
        Assert.That(row.Find("Follow User Button").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Unfollow"));
        row.Find("View profile").GetComponent<Button>().onClick.Invoke();
        Assert.That(service.LastTab, Is.EqualTo(SocialTab.Profile));
        Assert.That(service.LastTarget, Is.EqualTo("other"));
        panel.Back(); Assert.That(service.LastTab, Is.EqualTo(SocialTab.Activity));
        panel.Back(); Assert.That(panel.gameObject.activeSelf, Is.False);
    }

    [Test]
    public async Task SharingChangesOnlyWhenExplicitlyClickedAndSearchUsesCode()
    {
        await panel.RefreshAsync();
        Assert.That(service.SharingWrites, Is.Zero);
        Field<Button>("sharing").onClick.Invoke();
        Assert.That(service.SharingWrites, Is.EqualTo(1));
        Assert.That(service.Sharing, Is.True);
        Field<TMP_InputField>("codeInput").text = "ABCD-EFGH";
        Field<Button>("search").onClick.Invoke();
        Assert.That(service.LastTarget, Is.EqualTo("ABCD-EFGH"));
    }

    [Test]
    public async Task WalkDetailsOpensSelectedSummaryWithoutRefreshingAndBackKeepsTheList()
    {
        await panel.RefreshAsync();
        panel.ShowFollowers();
        var content = Field<RectTransform>("content");
        content.Find("Social player other/View profile").GetComponent<Button>().onClick.Invoke();
        var profileButton = content.Find("Social player other/View profile").GetComponent<Button>();
        Assert.That(profileButton.interactable, Is.False);
        Assert.That(profileButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Viewing walks"));
        var walkButton = content.Find("Activity other walk/View profile").GetComponent<Button>();
        Assert.That(walkButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("View details"));
        int reads = service.Reads;
        var scrollPosition = Field<ScrollRect>("scroll").verticalNormalizedPosition;
        walkButton.onClick.Invoke();
        var popup = Field<SocialWalkDetailsUI>("walkDetails");
        Assert.That(popup.gameObject.activeSelf, Is.True);
        Assert.That(popup.transform.Find("Walk details/Walker").GetComponent<TMP_Text>().text, Is.EqualTo("Other Walker"));
        Assert.That(popup.transform.Find("Walk details/Distance").GetComponent<TMP_Text>().text, Does.Contain("800 m"));
        Assert.That(popup.transform.Find("Walk details/Steps").GetComponent<TMP_Text>().text, Does.Contain("1,000"));
        Assert.That(popup.transform.Find("Walk details/Duration").GetComponent<TMP_Text>().text, Does.Contain("10 min 0 sec"));
        Assert.That(Field<CanvasGroup>("backgroundControls").interactable, Is.False);
        panel.Back();
        Assert.That(popup.gameObject.activeSelf, Is.False);
        Assert.That(panel.gameObject.activeSelf, Is.True);
        Assert.That(service.Reads, Is.EqualTo(reads));
        Assert.That(Field<CanvasGroup>("backgroundControls").interactable, Is.True);
        Assert.That(Field<ScrollRect>("scroll").verticalNormalizedPosition, Is.EqualTo(scrollPosition));
        Assert.That(content.Find("Activity other walk/View profile").GetComponent<Button>(), Is.SameAs(walkButton));
        walkButton.onClick.Invoke();
        popup.transform.Find("Walk details/Close walk details").GetComponent<Button>().onClick.Invoke();
        Assert.That(popup.gameObject.activeSelf, Is.False);
    }

    [Test]
    public async Task PopupUsesClickedWalkAndClearsOnSignOutOrPanelClose()
    {
        service.Read = () => {
            var data = service.Data();
            data.Activities.Add(new SocialActivity { Id = "zero", Player = data.Players[0],
                EndedUtc = new DateTime(2026, 9, 26, 11, 46, 0, DateTimeKind.Utc), DurationSeconds = 25 });
            return Task.FromResult(data);
        };
        await panel.RefreshAsync();
        Field<RectTransform>("content").Find("Activity other zero/View profile").GetComponent<Button>().onClick.Invoke();
        var popup = Field<SocialWalkDetailsUI>("walkDetails");
        Assert.That(popup.transform.Find("Walk details/Steps").GetComponent<TMP_Text>().text, Does.Contain("0"));
        Assert.That(popup.transform.Find("Walk details/Duration").GetComponent<TMP_Text>().text, Does.Contain("0 min 25 sec"));
        service.AuthenticatedUserId = ""; Invoke("Update");
        Assert.That(popup.gameObject.activeSelf, Is.False);
        Assert.That(popup.transform.Find("Walk details/Walker").GetComponent<TMP_Text>().text, Is.Empty);
        service.AuthenticatedUserId = "me"; await panel.RefreshAsync();
        Field<RectTransform>("content").Find("Activity other zero/View profile").GetComponent<Button>().onClick.Invoke();
        panel.gameObject.SetActive(false); Invoke("OnDisable");
        Assert.That(popup.gameObject.activeSelf, Is.False);
        Assert.That(Field<CanvasGroup>("backgroundControls").interactable, Is.True);
    }

    [Test]
    public async Task AccountChangeDuringServiceInitializationCannotApplyOldUsersMutation()
    {
        await panel.RefreshAsync();
        var initialization = new TaskCompletionSource<ISocialService>();
        panel.ServiceFactory = () => initialization.Task;
        bool wrote = false;
        var request = panel.RefreshAsync(_ => { wrote = true; return Task.CompletedTask; });
        service.AuthenticatedUserId = "new-user";
        initialization.SetResult(service); await request;
        Assert.That(wrote, Is.False);
        Assert.That(panel.StatusText, Does.Contain("Account changed"));
        Assert.That(Field<TMP_Text>("code").text, Is.Empty);
    }

    [Test]
    public void SearchViewportStaysInsideItsFieldAndAvatarDoesNotCoverTheScreen()
    {
        var input = Field<TMP_InputField>("codeInput");
        Assert.That(input.textViewport.IsChildOf(input.transform), Is.True);
        Assert.That(input.textViewport, Is.Not.SameAs(input.transform));
        var picture = Field<ProfilePhotoUI>("photo").GetComponent<RectTransform>();
        Assert.That(picture.anchorMax.x - picture.anchorMin.x, Is.LessThan(.25f));
        Assert.That(picture.GetComponent<AspectRatioFitter>(), Is.Null);
    }

    [Test]
    public async Task ClosedPanelDiscardsLateReadsAndClearsIdentity()
    {
        await panel.RefreshAsync();
        var late = new TaskCompletionSource<SocialSnapshot>(); service.Read = () => late.Task;
        var request = panel.RefreshAsync();
        panel.gameObject.SetActive(false); Invoke("OnDisable");
        late.SetResult(service.Data()); await request;
        Assert.That(panel.VisibleCardCount, Is.Zero);
        Assert.That(Field<TMP_Text>("code").text, Is.Empty);
        Assert.That(Field<TMP_Text>("profileName").text, Is.EqualTo("Your circle"));
    }

    [Test]
    public async Task NewerRefreshWinsAndAccountSwitchClearsOldCards()
    {
        var late = new TaskCompletionSource<SocialSnapshot>(); service.Read = () => late.Task;
        var request = panel.RefreshAsync();
        service.Read = null; await panel.RefreshAsync();
        late.SetResult(new SocialSnapshot { Me = new SocialPlayer { Id = "old", Name = "Old response" } }); await request;
        Assert.That(Field<TMP_Text>("profileName").text, Is.EqualTo("My Walker"));
        service.AuthenticatedUserId = ""; Invoke("Update");
        Assert.That(panel.VisibleCardCount, Is.Zero);
        Assert.That(panel.StatusText, Does.Contain("Sign in"));
        Assert.That(Field<TMP_Text>("code").text, Is.Empty);
        Assert.That(Field<Button>("sharing").interactable, Is.False);
    }

    [Test]
    public async Task NetworkErrorsKeepRefreshAvailableWithoutInventingEmptyScores()
    {
        service.Read = () => Task.FromException<SocialSnapshot>(new TimeoutException());
        LogAssertWarning();
        await panel.RefreshAsync();
        Assert.That(panel.StatusText, Does.Contain("Couldn't load"));
        Assert.That(Field<Button>("refresh").interactable, Is.True);
        Assert.That(Field<Button>("sharing").interactable, Is.False);
    }
    private static void LogAssertWarning() => UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "Social request failed: TimeoutException");

    private sealed class FakeSocial : ISocialService
    {
        public string AuthenticatedUserId { get; set; } = "me";
        public SocialTab LastTab; public string LastTarget;
        public bool Following, Sharing; public int SharingWrites, Reads;
        public Func<Task<SocialSnapshot>> Read;
        public Task<SocialSnapshot> LoadSocialAsync(SocialTab tab, string target, CancellationToken token)
        { Reads++; LastTab = tab; LastTarget = target; return Read != null ? Read() : Task.FromResult(Data()); }
        public Task SetFollowingAsync(string target, bool following, CancellationToken token)
        { Following = following; return Task.CompletedTask; }
        public Task SetActivitySharingAsync(bool sharing, CancellationToken token)
        { Sharing = sharing; SharingWrites++; return Task.CompletedTask; }
        public SocialSnapshot Data()
        {
            var other = new SocialPlayer { Id = "other", Name = "Other Walker", FollowsYou = true, Following = Following, SharesActivity = true };
            var data = new SocialSnapshot { Me = new SocialPlayer { Id = "me", Name = "My Walker", SharesActivity = Sharing },
                Code = "ABCD-EFGH", FollowingCount = 1, FollowerCount = 2, Profile = LastTab == SocialTab.Profile ? other : null };
            data.Players.Add(other);
            if (LastTab == SocialTab.Activity || LastTab == SocialTab.Profile)
                data.Activities.Add(new SocialActivity { Id = "walk", Player = other, EndedUtc = DateTime.UtcNow, Steps = 1000, DistanceMeters = 800, DurationSeconds = 600 });
            return data;
        }
    }
}
