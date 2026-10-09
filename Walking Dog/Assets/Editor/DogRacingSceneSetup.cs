using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WalkingDog.Racing;

// Explicit scene setup. All screens and controls are saved as ordinary editable
// Unity objects; importing scripts never silently modifies the scene.
public static class DogRacingSceneSetup
{
    private const string ScenePath = "Assets/Scenes/StepCounterTestAmar.unity";
    private static readonly Color Ink = new Color(.20f,.16f,.13f), Paper = new Color(.98f,.96f,.91f);
    private static readonly Color Green = new Color(.25f,.40f,.30f), Gold = new Color(.87f,.60f,.22f);
    private static TMP_FontAsset font;

    [MenuItem("Walking Dog/Connect dog race and training screens")]
    public static void Connect()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var canvas = GameObject.Find("Canvas");
        var controller = canvas.GetComponent<DogGameScreens>() ?? canvas.AddComponent<DogGameScreens>();
        foreach (var name in new[] { "Dog Race Screen", "Dog Training Screen" })
        {
            var old = canvas.transform.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI-UX/Font/Fredoka-Regular SDF.asset");
        if (font == null) font = TMP_Settings.defaultFontAsset;
        var training = Screen(canvas.transform, "Dog Training Screen");
        var racing = Screen(canvas.transform, "Dog Race Screen");
        Set(controller, "trainingScreen", training.gameObject); Set(controller, "raceScreen", racing.gameObject);
        var trainSafe = Safe(training);
        var raceSafe = Safe(racing);

        Label(trainSafe, "Eyebrow", "DOG TRAINING", .06f,.92f,.68f,.98f, 25, Green);
        Label(trainSafe, "Title", "A little stronger\nevery session.", .06f,.79f,.94f,.92f, 55, Ink);
        Button(trainSafe, "Back", "Back", .76f,.925f,.94f,.975f, controller.Close, Green);
        Set(controller, "points", Label(trainSafe, "Wallet", "Wallet points", .06f,.72f,.94f,.77f, 32, Ink));
        var dogCard = Panel(trainSafe, "Your dog", .06f,.56f,.94f,.70f, new Color(.92f,.90f,.82f));
        Dog(dogCard, "Dog icon", .04f,.12f,.27f,.91f, Gold);
        Set(controller, "dogSummary", Label(dogCard, "Dog summary", "Your dog", .32f,.54f,.96f,.88f, 31, Ink));
        Label(dogCard, "Explanation", "Stats carry into your next race.", .32f,.12f,.96f,.50f, 23, Ink);
        string[] titles = { "Speed", "Stamina", "Acceleration" };
        string[] descriptions = { "A faster cruising pace.", "Hold your pace for longer.", "Reach your pace sooner." };
        var rows = new DogGameScreens.StatRow[3];
        for (int i = 0; i < rows.Length; i++)
        {
            float top = .535f - i * .126f;
            var row = Panel(trainSafe, titles[i] + " training", .06f,top-.113f,.94f,top, Color.white);
            Label(row, "Stat", titles[i], .04f,.58f,.45f,.93f, 30, Ink);
            Label(row, "Effect", descriptions[i], .04f,.08f,.64f,.32f, 20, Ink);
            var value = Label(row, "Value", "— / 100", .45f,.58f,.67f,.93f, 25, Ink);
            var bar = Bar(row, .04f,.38f,.65f,.49f);
            UnityEngine.Events.UnityAction action = i == 0 ? controller.TrainSpeed : i == 1 ? controller.TrainStamina : controller.TrainAcceleration;
            var train = Button(row, "Train", "+5\n20 points", .70f,.12f,.96f,.89f, action, Green);
            rows[i] = new DogGameScreens.StatRow { stat = (DogStat)i, value = value, bar = bar, train = train };
        }
        Set(controller, "stats", rows);
        Set(controller, "trainingStatus", Label(trainSafe, "Status", "Load your dog to start training.", .06f,.09f,.94f,.15f, 23, Ink));
        Set(controller, "refreshTraining", Button(trainSafe, "Refresh", "Refresh", .06f,.025f,.45f,.075f, controller.RefreshTraining, Green));
        Button(trainSafe, "Practice", "Go to race", .49f,.025f,.94f,.075f, controller.OpenRace, Green);

        Label(raceSafe, "Eyebrow", "PRACTICE RACE", .06f,.92f,.68f,.98f, 25, Green);
        Label(raceSafe, "Title", "Ready. Set.\nLet them run.", .06f,.79f,.94f,.92f, 55, Ink);
        Button(raceSafe, "Back", "Back", .76f,.925f,.94f,.975f, controller.Close, Green);
        Set(controller, "raceSummary", Label(raceSafe, "Your stats", "Load your dog to race.", .06f,.72f,.94f,.78f, 25, Ink));
        Label(raceSafe, "Start label", "START", .06f,.675f,.30f,.71f, 20, Green);
        Label(raceSafe, "Finish label", "FINISH", .72f,.675f,.885f,.71f, 20, Green).alignment = TextAlignmentOptions.Right;
        var lanes = new DogGameScreens.RaceLane[4];
        var colors = new[] { Gold, new Color(.76f,.37f,.31f), new Color(.33f,.51f,.71f), new Color(.51f,.47f,.66f) };
        var entrants = DogRaceSimulation.PracticeField(new DogStats());
        for (int i = 0; i < lanes.Length; i++)
        {
            float top = .67f - i * .095f;
            var lane = Panel(raceSafe, "Lane " + i, .06f,top-.085f,.94f,top, i % 2 == 0 ? new Color(.87f,.91f,.82f) : new Color(.92f,.94f,.87f));
            var track = Panel(lane, "Track", .01f,.04f,.99f,.72f, Color.clear);
            Panel(track, "Finish", .94f,0,.947f,1, Green);
            var marker = new GameObject("Dog runner", typeof(RectTransform), typeof(CanvasRenderer), typeof(DogIconGraphic)).GetComponent<RectTransform>();
            marker.SetParent(track, false);
            marker.anchorMin = marker.anchorMax = new Vector2(.08f,.5f); marker.sizeDelta = new Vector2(70,58);
            marker.GetComponent<DogIconGraphic>().color = new Color(.58f,.38f,.23f);
            marker.GetComponent<DogIconGraphic>().collarColor = colors[i]; marker.GetComponent<DogIconGraphic>().raycastTarget = false;
            var label = Label(lane, "Runner stats", entrants[i].Name + "  ·  40 / 40 / 40", .025f,.73f,.98f,.99f, 20, Ink);
            lanes[i] = new DogGameScreens.RaceLane { marker = marker, details = label };
        }
        Set(controller, "lanes", lanes);
        Set(controller, "raceStatus", Label(raceSafe, "Race status", "200 m · Three simulated training partners", .06f,.23f,.94f,.285f, 22, Ink));
        Set(controller, "results", Label(raceSafe, "Results", "Speed sets pace. Acceleration helps the start.\nStamina delays fatigue.", .06f,.105f,.94f,.225f, 24, Ink));
        Set(controller, "startRace", Button(raceSafe, "Start race", "Start race", .06f,.025f,.55f,.082f, controller.StartRace, Green));
        Button(raceSafe, "Training", "Training", .59f,.025f,.94f,.082f, controller.OpenTraining, Green);

        var menu = canvas.transform.Find("Walk Screen Safe Area/SideArea/Buttons");
        if (menu == null) menu = canvas.GetComponentsInChildren<Transform>(true).First(t => t.name == "SideArea").Find("Buttons");
        foreach (var name in new[] { "Race Button", "Training Button" })
        { var old = menu.Find(name); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }
        // Keep all eight navigation buttons inside the existing side panel.
        var grid = menu.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(125,125); grid.spacing = new Vector2(0,18);
        Button(menu, "Race Button", "Race", 0,0,1,1, controller.OpenRace, Green);
        Button(menu, "Training Button", "Training", 0,0,1,1, controller.OpenTraining, Green);
        training.gameObject.SetActive(false); racing.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Dog race and training screens connected.");
    }

    private static RectTransform Screen(Transform parent, string name) => Panel(parent, name, 0,0,1,1, Paper);
    private static RectTransform Safe(Transform parent)
    { var safe = Panel(parent, "Safe Area",0,0,1,1,Color.clear); safe.GetComponent<Image>().raycastTarget = false; safe.gameObject.AddComponent<WalkScreenSafeArea>(); return safe; }
    private static RectTransform Panel(Transform parent, string name, float x, float y, float x2, float y2, Color tint)
    {
        var rect = new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
        Place(rect,x,y,x2,y2); rect.GetComponent<Image>().color = tint; return rect;
    }
    private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float x2, float y2, int size, Color tint)
    {
        var label = new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent,false); Place(label.rectTransform,x,y,x2,y2);
        label.font = font; label.text = text; label.fontSize = size; label.enableAutoSizing = true;
        label.fontSizeMin = Math.Max(14,size-7); label.fontSizeMax = size; label.color = tint;
        label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.Normal; return label;
    }
    private static Button Button(Transform parent, string name, string title, float x, float y, float x2, float y2, UnityEngine.Events.UnityAction action, Color tint)
    {
        var rect = Panel(parent,name,x,y,x2,y2,tint); var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>(); button.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = Label(rect,"Label",title,.06f,.08f,.94f,.92f,27,Color.white); label.alignment = TextAlignmentOptions.Center;
        UnityEventTools.AddPersistentListener(button.onClick,action); return button;
    }
    private static Slider Bar(Transform parent, float x, float y, float x2, float y2)
    {
        var rect = Panel(parent,"Stat bar",x,y,x2,y2,new Color(.87f,.89f,.84f)); var slider = rect.gameObject.AddComponent<Slider>();
        slider.minValue = 0; slider.maxValue = 100; slider.interactable = false;
        slider.fillRect = Panel(rect,"Fill",0,0,1,1,Gold); return slider;
    }
    private static void Dog(Transform parent, string name, float x, float y, float x2, float y2, Color collar)
    {
        var rect = new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(DogIconGraphic)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); Place(rect,x,y,x2,y2); var dog = rect.GetComponent<DogIconGraphic>();
        dog.color = new Color(.58f,.38f,.23f); dog.collarColor = collar; dog.raycastTarget = false;
    }
    private static void Place(RectTransform rect, float x, float y, float x2, float y2)
    { rect.anchorMin = new Vector2(x,y); rect.anchorMax = new Vector2(x2,y2); rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);

    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        var controller = canvas.GetComponent<DogGameScreens>();
        Set(controller,"snapshot",new DogStats { speed=55,stamina=45,acceleration=50,trainingCount=6 });
        Set(controller,"owner","preview"); Set(controller,"OwnerReader",(Func<string>)(() => "preview"));
        foreach (Transform child in canvas.transform) if (child.name != "Dog Training Screen" && child.name != "Dog Race Screen") child.gameObject.SetActive(false);
        var training = canvas.transform.Find("Dog Training Screen"); var racing = canvas.transform.Find("Dog Race Screen");
        training.gameObject.SetActive(true); Render(controller,"RenderStats");
        Get<TMP_Text>(controller,"points").text = "Wallet  240 points";
        Get<TMP_Text>(controller,"trainingStatus").text = "Choose a training session. Each costs 20 points and adds 5 to one stat.";
        Capture(canvas,"Logs/dog-training-preview.png",720,1280);
        Capture(canvas,"Logs/dog-training-tall-preview.png",946,2048);
        training.gameObject.SetActive(false); racing.gameObject.SetActive(true);
        var race = new DogRaceSimulation(DogRaceSimulation.PracticeField(Get<DogStats>(controller,"snapshot")),7); race.Advance(18);
        Set(controller,"race",race); Render(controller,"RenderRace");
        Capture(canvas,"Logs/dog-race-preview.png",720,1280);
        race.Advance(60); Render(controller,"RenderRace"); Capture(canvas,"Logs/dog-race-finish-preview.png",720,1280);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }
    public static void ConnectAndPreview() { Connect(); CapturePreview(); }
    private static T Get<T>(object target,string field) => (T)target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Render(object target,string method) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    private static void Capture(Canvas canvas,string path,int width,int height)
    {
        var cameraObject = new GameObject("Dog preview camera"); var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(width,height,24); camera.targetTexture = texture; camera.orthographic = true;
        camera.orthographicSize = 640; camera.transform.position = new Vector3(0,0,-10);
        canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = width / 720f;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
        var image = new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
        Directory.CreateDirectory("Logs"); File.WriteAllBytes(path,image.EncodeToPNG()); RenderTexture.active = null;
        camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
