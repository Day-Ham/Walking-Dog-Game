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

// Explicit, repeatable scene migration. Reuses the author's Profile Screen,
// header, list, card, and follow button; all extra controls stay editable.
public static class SocialSceneSetup
{
    [MenuItem("Walking Dog/Connect follow and activity UI")]
    public static void Connect()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var root = canvas.transform.Find("Profile Screen");
        root.gameObject.SetActive(false);
        var safe = root.Find("Safe Area");
        Add<WalkScreenSafeArea>(safe.gameObject);
        Place(safe, 0, 0, 1, 1);
        var panel = Add<SocialPanelUI>(root.gameObject);
        Set(panel, "owner", canvas.transform.Find("Leaderboard Screen").GetComponent<LeaderboardPanelUI>());
        var name = safe.Find("Profile Desc/Profile Name").GetComponent<TMP_Text>();
        var followingCount = safe.Find("Profile Desc/Following Amount Display").GetComponent<TMP_Text>();
        var followerCount = safe.Find("Profile Desc/Follower Amount Display").GetComponent<TMP_Text>();
        var picture = safe.Find("Profile Picture");
        var aspect = picture.GetComponent<AspectRatioFitter>();
        if (aspect != null) UnityEngine.Object.DestroyImmediate(aspect);
        Place(picture, .055f, .82f, .22f, .925f);
        Place(safe.Find("Profile Desc"), .26f, .82f, .95f, .925f);
        Place(name.transform, .02f, .48f, .98f, .96f); Style(name, 46);
        Place(followingCount.transform, .02f, .06f, .48f, .42f); Style(followingCount, 29);
        Place(followerCount.transform, .52f, .06f, .98f, .42f); Style(followerCount, 29);
        Set(panel, "profileName", name); Set(panel, "followingCount", followingCount); Set(panel, "followerCount", followerCount);
        Set(panel, "photo", Add<ProfilePhotoUI>(picture.gameObject));
        name.text = "Your circle"; followingCount.text = "Following"; followerCount.text = "Followers";
        var back = safe.Find("BACK").GetComponent<Button>();
        ConfigureButton(back, "Back", .74f, .943f, .95f, .985f);
        Set(panel, "back", back);
        Label(safe, name, "Social title", "WALK TOGETHER", .05f, .943f, .70f, .985f, 32);
        var discover = safe.Find("Display Users").GetComponent<Button>();
        var following = safe.Find("Display Following Users").GetComponent<Button>();
        var followers = safe.Cast<Transform>().First(t => t.name.Trim() == "Display Follower Users").GetComponent<Button>();
        ConfigureButton(discover, "Discover", .28f, .695f, .49f, .745f);
        ConfigureButton(following, "Following", .51f, .695f, .72f, .745f);
        ConfigureButton(followers, "Followers", .74f, .695f, .95f, .745f);
        Set(panel, "discover", discover); Set(panel, "following", following); Set(panel, "followers", followers);
        Set(panel, "activity", Button(safe, discover, "Activity tab", "Activity", .05f, .695f, .26f, .745f));
        Set(panel, "refresh", Button(safe, discover, "Social refresh", "Refresh", .73f, .01f, .95f, .06f));
        Set(panel, "sharing", Button(safe, discover, "Share walks", "Walk sharing: OFF", .60f, .76f, .95f, .803f));
        Set(panel, "copy", Button(safe, discover, "Copy player code", "Copy code", .33f, .76f, .57f, .803f));
        Set(panel, "code", Label(safe, name, "Player code", "", .05f, .76f, .31f, .803f, 27));
        Label(safe, name, "Sharing explanation", "Sharing ON lets followers see your past and future synced walk summaries.", .05f, .593f, .95f, .63f, 25);
        Label(safe, name, "Social footer", "Walk summaries · GPS routes stay private", .05f, .01f, .70f, .06f, 23);
        Set(panel, "status", Label(safe, name, "Social status", "Sign in to follow walkers.", .05f, .55f, .95f, .588f, 26));
        var oldInput = safe.Find("Find player code");
        if (oldInput != null) UnityEngine.Object.DestroyImmediate(oldInput.gameObject);
        var input = new GameObject("Find player code", typeof(RectTransform), typeof(Image), typeof(TMP_InputField)).GetComponent<TMP_InputField>();
        input.transform.SetParent(safe, false);
        input.GetComponent<Image>().color = new Color(.08f, .16f, .20f);
        input.targetGraphic = input.GetComponent<Image>();
        var textArea = new GameObject("Text area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        textArea.SetParent(input.transform, false);
        input.textViewport = textArea;
        input.textComponent = (TextMeshProUGUI)Label(textArea, name, "Input text", "", 0, 0, 1, 1, 28);
        Place(input.transform, .05f, .638f, .72f, .683f);
        input.onValueChanged = new TMP_InputField.OnChangeEvent(); input.onEndEdit = new TMP_InputField.SubmitEvent();
        input.characterLimit = 128; input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.SetTextWithoutNotify("");
        Place(input.textViewport, .035f, .05f, .965f, .95f);
        Place(input.textComponent.transform, 0, 0, 1, 1); Style(input.textComponent, 28);
        var placeholder = Label(input.textViewport, name, "Player code placeholder", "Enter a player code", 0, 0, 1, 1, 28);
        placeholder.color = new Color(.75f, .8f, .85f); input.placeholder = placeholder;
        Set(panel, "codeInput", input);
        Set(panel, "search", Button(safe, discover, "Find player", "Find", .75f, .638f, .95f, .683f));
        var scroll = safe.Find("Profile or Post List View").GetComponent<ScrollRect>();
        Place(scroll.transform, .05f, .08f, .95f, .54f);
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        Place(scroll.viewport, 0, 0, 1, 1);
        var content = scroll.content;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.anchoredPosition = content.sizeDelta = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 16; layout.padding = new RectOffset(16, 16, 16, 16);
        layout.childControlHeight = layout.childControlWidth = layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        Add<ContentSizeFitter>(content.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var template = content.Cast<Transform>().First(t => t.name.Trim() == "Profile Template");
        template.gameObject.SetActive(false);
        var element = Add<LayoutElement>(template.gameObject); element.enabled = true; element.preferredHeight = 250;
        var card = Add<SocialCardUI>(template.gameObject);
        var cardName = template.Find("Name").GetComponent<TMP_Text>();
        Place(cardName.transform, .21f, .72f, .96f, .94f); Style(cardName, 34);
        Place(template.Find("Profile Pic"), .025f, .40f, .18f, .91f);
        Set(card, "photo", Add<ProfilePhotoUI>(template.Find("Profile Pic").gameObject));
        Set(card, "playerName", cardName);
        Set(card, "details", Label(template, name, "Activity details", "", .21f, .37f, .97f, .70f, 27));
        var follow = template.Find("Follow User Button").GetComponent<Button>();
        ConfigureButton(follow, "Follow", .53f, .055f, .95f, .34f);
        Set(card, "follow", follow);
        Set(card, "view", Button(template, follow, "View profile", "View walks", .05f, .055f, .47f, .34f));
        Set(panel, "content", content); Set(panel, "scroll", scroll); Set(panel, "template", card);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Connected existing Profile Screen to Firebase social UI.");
    }

    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StepCounterTestAmar.unity");
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var root = canvas.transform.Find("Profile Screen");
        foreach (Transform sibling in canvas.transform) sibling.gameObject.SetActive(sibling == root);
        var safe = root.Find("Safe Area").GetComponent<WalkScreenSafeArea>();
        safe.enabled = false; Place(safe.transform, 0, 0, 1, 1);
        var panel = root.GetComponent<SocialPanelUI>();
        var me = new SocialPlayer { Id = "preview-me", Name = "Jamie Walker", SharesActivity = true };
        var other = new SocialPlayer { Id = "preview-other", Name = "Luna and Mochi", Following = true, FollowsYou = true, SharesActivity = true };
        var snapshot = new SocialSnapshot { Me = me, Code = "ABCD-EFGH", FollowingCount = 12, FollowerCount = 8 };
        snapshot.Players.Add(other);
        snapshot.Activities.Add(new SocialActivity { Id = "preview-walk", Player = other, EndedUtc = DateTime.UtcNow.AddHours(-1), Steps = 4230, DistanceMeters = 2810, DurationSeconds = 2040 });
        snapshot.Activities.Add(new SocialActivity { Id = "preview-walk-own", Player = me, EndedUtc = DateTime.UtcNow.AddHours(-3), Steps = 2615, DistanceMeters = 1750, DurationSeconds = 1320 });
        typeof(SocialPanelUI).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, new object[] { snapshot });
        Directory.CreateDirectory("Logs");
        foreach (var size in new[] { new Vector2Int(720,1280), new Vector2Int(946,2048) })
            Capture(canvas, $"Logs/social-activity-{size.x}x{size.y}.png", size.x, size.y);
        root.Find("Safe Area/Profile or Post List View/Viewport/post or profile Content/Activity preview-other preview-walk/View profile")
            .GetComponent<Button>().onClick.Invoke();
        foreach (var size in new[] { new Vector2Int(720,1280), new Vector2Int(946,2048) })
            Capture(canvas, $"Logs/social-walk-details-{size.x}x{size.y}.png", size.x, size.y);
        panel.Back();
        Set(panel, "tab", SocialTab.Followers); snapshot.Activities.Clear();
        typeof(SocialPanelUI).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, new object[] { snapshot });
        Capture(canvas, "Logs/social-followers-720x1280.png", 720, 1280);
        // Preview data is never saved to the scene or sent to Firebase.
    }

    public static void ConnectAndPreview() { Connect(); CapturePreview(); }

    private static void Capture(Canvas canvas, string path, int width, int height)
    {
        var go = new GameObject("Social preview camera"); var camera = go.AddComponent<Camera>();
        var rt = new RenderTexture(width, height, 24); camera.targetTexture = rt;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture.active = rt; var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
    }
    private static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    private static void Place(Transform transform, float x, float y, float x2, float y2)
    {
        var rect = (RectTransform)transform; rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(x, y); rect.anchorMax = new Vector2(x2, y2); rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static void Style(TMP_Text text, float max)
    {
        text.margin = Vector4.zero; text.enableAutoSizing = true; text.fontSizeMin = 18; text.fontSizeMax = max;
        text.richText = false; text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft;
    }
    private static TMP_Text Label(Transform parent, TMP_Text source, string name, string value, float x, float y, float x2, float y2, float size)
    {
        var label = parent.Find(name)?.GetComponent<TMP_Text>();
        if (label == null) { label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>(); label.transform.SetParent(parent, false); }
        label.font = source.font; label.color = source.color; label.text = value; Style(label, size); Place(label.transform, x, y, x2, y2);
        return label;
    }
    private static Button Button(Transform parent, Button source, string name, string label, float x, float y, float x2, float y2)
    {
        var button = parent.Find(name)?.GetComponent<Button>() ?? UnityEngine.Object.Instantiate(source, parent);
        button.name = name; ConfigureButton(button, label, x, y, x2, y2); return button;
    }
    private static void ConfigureButton(Button button, string label, float x, float y, float x2, float y2)
    {
        button.onClick = new Button.ButtonClickedEvent(); button.interactable = true;
        Place(button.transform, x, y, x2, y2);
        var text = button.GetComponentInChildren<TMP_Text>(true); text.text = label; Style(text, 29);
        text.alignment = TextAlignmentOptions.Center; Place(text.transform, .035f, .05f, .965f, .95f);
    }
}
