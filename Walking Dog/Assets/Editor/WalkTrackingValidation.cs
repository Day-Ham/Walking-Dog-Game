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

    private static void Capture(Canvas canvas, string path)
    {
        var cameraObject = new GameObject("Preview camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(720, 1280, 24);
        camera.targetTexture = texture;
        camera.orthographic = true;
        camera.orthographicSize = 640;
        camera.transform.position = new Vector3(0, 0, -10);
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.scaleFactor = 1;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(720, 1280, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
