using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Batch validation entry points; never included in the Android player.
public static class WalkTrackingValidation
{
    public static void ValidateHistoryNavigation()
    {
        CaptureWalkLayout();
        var launch = GameObject.Find("History Button").GetComponent<Button>();
        var history = UnityEngine.Object.FindAnyObjectByType<WalkHistoryUI>();
        if (!launch.isActiveAndEnabled || !launch.interactable ||
            launch.onClick.GetPersistentEventCount() != 1 ||
            launch.onClick.GetPersistentTarget(0) != history ||
            launch.onClick.GetPersistentMethodName(0) != nameof(WalkHistoryUI.Open))
            throw new Exception("History button is missing or incorrectly wired.");

        var liveMap = GameObject.Find("OpenFreeMap Map Panel").GetComponent<OpenFreeMapWebViewMap>();
        // Allow the serialized runtime callback to execute in this editor check.
        launch.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        launch.onClick.Invoke();
        var overlay = GameObject.Find("Walk History Canvas").GetComponent<Canvas>();
        var panel = overlay.transform.Find("Walk History").gameObject;
        var routePanel = overlay.transform.Find("Walk Map Panel").gameObject;
        if (!panel.activeSelf || routePanel.activeSelf || liveMap.enabled)
            throw new Exception("History did not open with the live map hidden.");
        Capture(overlay, "Logs/walk-history-open.png");
        panel.transform.Find("Safe area/Back").GetComponent<Button>().onClick.Invoke();
        if (panel.activeSelf || !liveMap.enabled || !launch.gameObject.activeInHierarchy)
            throw new Exception("History Back did not restore the walk screen.");
        launch.onClick.Invoke();
        if (!panel.activeSelf || UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Count(c => c.name == "Walk History Canvas") != 1)
            throw new Exception("Reopening history created a duplicate overlay.");
        history.Close();
        launch.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.RuntimeOnly);
        Debug.Log("History button callback, open, Back, and reopen checks passed.");
    }

    public static void CaptureWalkLayout()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var safe = canvas.GetComponentInChildren<WalkScreenSafeArea>();
        safe.enabled = false;
        var safeRect = safe.GetComponent<RectTransform>();
        safeRect.anchorMin = Vector2.zero;
        safeRect.anchorMax = Vector2.one;
        safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
        GameObject.Find("Counter").GetComponent<TMPro.TMP_Text>().text = "473";
        GameObject.Find("Longitude").GetComponent<TMPro.TMP_Text>().text = "473 steps • 291 m • 4.1 min";
        GameObject.Find("Latitude").GetComponent<TMPro.TMP_Text>().text = "Screen-off route recording on";
        GameObject.Find("Display Accuracy").GetComponent<TMPro.TMP_Text>().text = "Recording";
        GameObject.Find("Walking Session Button").GetComponentInChildren<TMPro.TMP_Text>().text = "Stop Walk";
        Directory.CreateDirectory("Logs");
        foreach (var size in new[] { new Vector2Int(720, 1280), new Vector2Int(946, 2048), new Vector2Int(1536, 2048) })
        {
            Capture(canvas, $"Logs/walk-layout-{size.x}x{size.y}.png", size.x, size.y);
            foreach (var label in safe.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                label.ForceMeshUpdate();
                if (label.isTextOverflowing) throw new Exception($"Walk HUD overflow: {label.name} at {size}");
            }
        }
        // Exercise the longer error/status messages as well as the screenshot values.
        GameObject.Find("Longitude").GetComponent<TMPro.TMP_Text>().text = "123,456 steps • 123.45 km • 999.9 min";
        GameObject.Find("Latitude").GetComponent<TMPro.TMP_Text>().text = "Screen-off tracking unavailable. Keep the app open.";
        GameObject.Find("Display Accuracy").GetComponent<TMPro.TMP_Text>().text = "Recovery save failed — keep the app open";
        Capture(canvas, "Logs/walk-layout-long-messages.png");
        foreach (var label in safe.GetComponentsInChildren<TMPro.TMP_Text>())
        {
            label.ForceMeshUpdate();
            if (label.isTextOverflowing) throw new Exception("Walk HUD overflow: " + label.name);
        }
        Debug.Log("Walk layout captures and text fit checks passed.");
    }

    public static void BuildAndroid()
    {
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = "Builds/WalkingDog-tracking.apk",
            target = BuildTarget.Android,
            options = BuildOptions.Development
        };
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Android build failed: " + report.summary.result);
        Debug.Log("Walking Dog Android validation build succeeded.");
    }

    public static void CaptureDialogs()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var host = new GameObject("Preview");
        var manager = host.AddComponent<StepCountAndGpsManager>();
        typeof(StepCountAndGpsManager).GetProperty("Instance").SetValue(null, manager);
        Set(manager, "hasWalkingSession", true);
        Set(manager, "sessionEndSteps", 824);
        Set(manager, "sessionDistanceMeters", 612f);
        Set(manager, "sessionEndTime", 540f);
        Set(manager, "lastSavedWalkFilePath", "preview");
        Set(manager, "lastWalkSaveState", "Saved on device");
        var ui = host.AddComponent<WalkRecoveryUI>();
        ui.ShowSummary();
        var canvas = GameObject.Find("Walk Recovery Canvas").GetComponent<Canvas>();
        Capture(canvas, "Logs/walk-summary-preview.png");
        Set(ui, "summary", false);
        Set(ui, "pending", new StepCountAndGpsManager.SavedWalkSession
        { steps = 824, distanceMeters = 612, endedAtUtc = "2026-09-10T05:30:00Z" });
        typeof(WalkRecoveryUI).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
        Capture(canvas, "Logs/walk-recovery-preview.png");
        UnityEngine.Object.DestroyImmediate(host);
        UnityEngine.Object.DestroyImmediate(canvas.gameObject);
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Capture(Canvas canvas, string path, int width = 720, int height = 1280)
    {
        var cameraObject = new GameObject("Preview camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(width, height, 24);
        camera.targetTexture = texture;
        camera.orthographic = true;
        camera.orthographicSize = 640;
        camera.transform.position = new Vector3(0, 0, -10);
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.scaleFactor = width / canvas.GetComponent<CanvasScaler>().referenceResolution.x;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
