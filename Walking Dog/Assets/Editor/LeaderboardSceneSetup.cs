using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WalkingDog.Leaderboards;

// Explicit editor command, never an automatic import hook. Keeps the merged
// background, title, player-card artwork and horizontal browsing design.
public static class LeaderboardSceneSetup
{
    private const string ScenePath = "Assets/Scenes/StepCounterTestAmar.unity";

    [MenuItem("Walking Dog/Connect leaderboard UI")]
    public static void Connect()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var panel = canvas.transform.Find("Leaderboard Screen");
        var safe = panel.Find("LeaderBG/LeaderSafeArea");
        var title = safe.Find("Title").GetComponent<TMP_Text>();
        var back = safe.Find("Back").GetComponent<Button>();
        var refresh = safe.Find("RefreshLeaders").GetComponent<Button>();
        var scroll = safe.Find("LeaderList").GetComponent<ScrollRect>();
        var content = safe.Find("LeaderList/Viewport/Leaders").GetComponent<RectTransform>();
        var template = content.Find("User Template");
        var card = GetOrAdd<LeaderboardCardUI>(template.gameObject);
        Set(card, "playerName", template.Find("Name").GetComponent<TMP_Text>());
        Set(card, "details", template.Find("Score details").GetComponent<TMP_Text>());
        template.gameObject.SetActive(false);

        // The original safe-area object had a fixed negative size, clipping on
        // shorter phones. Keep the existing elements with proportional anchors.
        GetOrAdd<WalkScreenSafeArea>(safe.gameObject);
        Place(safe, 0, 0, 1, 1);
        Place(title.transform, .12f, .82f, .88f, .90f);
        Place(back.transform, .70f, .93f, .94f, .98f);
        Place(scroll.transform, .10f, .30f, .90f, .74f);
        Place(refresh.transform, .10f, .025f, .47f, .077f);
        refresh.GetComponentInChildren<TMP_Text>().text = "Refresh";
        Place(canvas.transform.Find("Walk Screen Safe Area/Leaderboard Button"), .05f, .92f, .45f, .97f);

        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.content = content;
        scroll.viewport = safe.Find("LeaderList/Viewport").GetComponent<RectTransform>();
        var redundantMask = scroll.viewport.GetComponent<Mask>();
        if (redundantMask != null) UnityEngine.Object.DestroyImmediate(redundantMask);
        Place(scroll.viewport, 0, 0, 1, 1);
        scroll.viewport.offsetMin = new Vector2(8, 24);
        scroll.viewport.offsetMax = new Vector2(-8, -8);
        content.anchorMin = new Vector2(0, 0);
        content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, .5f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 24;
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;
        var fit = content.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        GetOrAdd<LayoutElement>(template.gameObject).preferredWidth = 440;
        Place(template.Find("Name"), .05f, .83f, .95f, .98f);
        Place(template.Find("Image"), .10f, .40f, .90f, .81f);
        template.Find("Image").GetComponent<Image>().preserveAspect = true;
        Place(template.Find("Score details"), .04f, .02f, .96f, .39f);
        foreach (var text in template.GetComponentsInChildren<TMP_Text>(true))
        { text.fontSizeMin = 24; text.fontSizeMax = 46; text.enableAutoSizing = true; text.characterSpacing = 0; }

        var distance = MakeButton(safe, refresh, "Distance", "Distance", .10f, .76f, .48f, .81f);
        var steps = MakeButton(safe, refresh, "Steps", "Steps", .52f, .76f, .90f, .81f);
        var status = MakeText(safe, title, "Leaderboard Status", 30);
        Place(status.transform, .10f, .245f, .90f, .29f);
        status.text = "Open the leaderboard to load rankings.";
        var own = MakeText(safe, title, "Your Totals", 32);
        Place(own.transform, .10f, .16f, .90f, .235f);
        own.text = "";
        var hint = MakeText(safe, title, "Nickname Hint", 24);
        hint.text = "Nickname · 3–24 letters, numbers, spaces, _ or -";
        Place(hint.transform, .10f, .122f, .90f, .15f);
        var inputObject = safe.Find("Nickname")?.gameObject ?? new GameObject("Nickname", typeof(RectTransform), typeof(Image));
        inputObject.transform.SetParent(safe, false);
        Place(inputObject.transform, .10f, .081f, .70f, .121f);
        inputObject.GetComponent<Image>().color = new Color(.09f, .08f, .18f, .95f);
        var input = GetOrAdd<TMP_InputField>(inputObject);
        var inputText = MakeText(inputObject.transform, title, "Text", 30);
        Place(inputText.transform, 0, 0, 1, 1);
        inputText.rectTransform.offsetMin = new Vector2(16, 0);
        inputText.rectTransform.offsetMax = new Vector2(-16, 0);
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        inputText.text = "";
        inputText.enableAutoSizing = false;
        input.textComponent = inputText;
        input.textViewport = inputObject.GetComponent<RectTransform>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.characterLimit = 24;
        input.lineType = TMP_InputField.LineType.SingleLine;
        var save = MakeButton(safe, refresh, "Save Nickname", "Save", .73f, .081f, .90f, .121f);
        var controller = GetOrAdd<LeaderboardPanelUI>(panel.gameObject);
        Set(controller, "content", content);
        Set(controller, "cardTemplate", card);
        Set(controller, "scroll", scroll);
        Set(controller, "status", status);
        Set(controller, "personalTotals", own);
        Set(controller, "refresh", refresh);
        Set(controller, "distance", distance);
        Set(controller, "steps", steps);
        Set(controller, "nickname", input);
        Set(controller, "saveNickname", save);
        // Visibility must follow the controller's root, not just its background.
        panel.Find("LeaderBG").gameObject.SetActive(true);
        var launch = canvas.transform.Find("Walk Screen Safe Area/Leaderboard Button").GetComponent<Button>();
        launch.onClick = new Button.ButtonClickedEvent();
        back.onClick = new Button.ButtonClickedEvent();
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(launch.onClick, panel.gameObject.SetActive, true);
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(back.onClick, panel.gameObject.SetActive, false);
        panel.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Connected the merged leaderboard screen to FirebaseLeaderboardService.");
    }

    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var panel = canvas.transform.Find("Leaderboard Screen");
        foreach (var safe in canvas.GetComponentsInChildren<WalkScreenSafeArea>(true)) safe.enabled = false;
        panel.gameObject.SetActive(true);
        var controller = panel.GetComponent<LeaderboardPanelUI>();
        var entries = new[] {
            new LeaderboardEntry("one", "Mochi Walker", 24600, 32180, 12),
            new LeaderboardEntry("me", "My Walking Dog", 18500, 26400, 9),
            new LeaderboardEntry("three", "Weekend Walker", 10200, 14500, 5)
        };
        typeof(LeaderboardPanelUI).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(controller, new object[] { new LeaderboardSnapshot(LeaderboardMetric.Distance, "me", entries, entries[1]) });
        foreach (var size in new[] { new Vector2Int(720, 1280), new Vector2Int(946, 2048) })
            Capture(canvas, "Logs/leaderboard-" + size.x + "x" + size.y + ".png", size.x, size.y);
        // Preview rows are never saved into the scene or sent to Firebase.
    }

    private static void Capture(Canvas canvas, string path, int width, int height)
    {
        Directory.CreateDirectory("Logs");
        var go = new GameObject("Leaderboard Preview Camera", typeof(Camera));
        var camera = go.GetComponent<Camera>();
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        camera.orthographic = true;
        camera.transform.position = new Vector3(0, 0, -10);
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.enabled = false;
        canvas.scaleFactor = width / scaler.referenceResolution.x;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        foreach (var group in canvas.GetComponentsInChildren<HorizontalLayoutGroup>())
            LayoutRebuilder.ForceRebuildLayoutImmediate(group.GetComponent<RectTransform>());
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Place(Transform transform, float left, float bottom, float right, float top)
    {
        var rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static TMP_Text MakeText(Transform parent, TMP_Text source, string name, float size)
    {
        var text = parent.Find(name)?.GetComponent<TMP_Text>();
        if (text == null) text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        text.transform.SetParent(parent, false);
        text.font = source.font;
        text.fontSize = size;
        text.fontSizeMin = 20;
        text.fontSizeMax = size;
        text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }
    private static Button MakeButton(Transform parent, Button source, string name, string label, float left, float bottom, float right, float top)
    {
        var button = parent.Find(name)?.GetComponent<Button>();
        if (button == null) button = UnityEngine.Object.Instantiate(source, parent);
        button.name = name;
        button.onClick = new Button.ButtonClickedEvent();
        Place(button.transform, left, bottom, right, top);
        var text = button.GetComponentInChildren<TMP_Text>();
        text.text = label;
        text.characterSpacing = 0;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20;
        text.fontSizeMax = 36;
        return button;
    }
}
