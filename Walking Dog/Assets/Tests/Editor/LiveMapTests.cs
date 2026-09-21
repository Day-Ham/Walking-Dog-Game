using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;

public sealed class LiveMapTests
{
    [TearDown]
    public void CleanUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [Test]
    public void RepeatedAndOverlappingOverlaysRestoreLiveMapOnlyAfterAllClose()
    {
        var map = new GameObject("Map", typeof(RectTransform)).AddComponent<OpenFreeMapWebViewMap>();
        var history = new GameObject("History");
        var dialog = new GameObject("Dialog");
        Assert.That(map.IsMapVisible, Is.True);
        map.Suspend(history);
        map.Suspend(history);
        map.Suspend(dialog);
        map.Resume(history);
        Assert.That(map.IsMapVisible, Is.False, "The second overlay still covers the map.");
        map.Resume(dialog);
        Assert.That(map.IsMapVisible, Is.True);
        Assert.That(map.enabled, Is.True, "Hiding native views must not disable map lifecycle updates.");
    }

    [Test]
    public void LiveMapBuildsPreviewWithoutGpsOrAuthentication()
    {
        var map = new GameObject("Map", typeof(RectTransform)).AddComponent<OpenFreeMapWebViewMap>();
        var state = typeof(OpenFreeMapWebViewMap).GetMethod("BuildMapState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, null);
        string json = JsonUtility.ToJson(state);
        Assert.That(json, Does.Contain("\"hasLocation\":false"));
        var latitude = (float)state.GetType().GetField("lat").GetValue(state);
        Assert.That(latitude, Is.EqualTo(14.5995f).Within(0.00001f));
        Assert.That(json, Does.Contain("\"follow\":true"));
    }

    [Test]
    public void WalkingSceneHasAnEnabledLiveMapSeparateFromHistory()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        var live = GameObject.Find("OpenFreeMap Map Panel").GetComponent<OpenFreeMapWebViewMap>();
        Assert.That(live, Is.Not.Null);
        Assert.That(live.enabled, Is.True);
        var leaderboard = GameObject.Find("Canvas").transform.Find("Leaderboard Screen");
        Assert.That(leaderboard.gameObject.activeSelf, Is.False,
            "A hidden leaderboard must not run OnEnable and suppress the walking map at startup.");
        Assert.That(leaderboard.Find("LeaderBG").gameObject.activeSelf, Is.True,
            "Opening the leaderboard root must reveal its content.");
        var history = Object.FindAnyObjectByType<WalkHistoryUI>();
        var historyMap = typeof(WalkHistoryUI).GetField("openFreeMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(history);
        Assert.That(historyMap, Is.Not.SameAs(live));
    }
}
