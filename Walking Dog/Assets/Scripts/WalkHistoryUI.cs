using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Scene-local view. Attached to the walking scene Canvas; builds its own canvas
// so the history layout is independent of the existing tracking panel's scale.
[DisallowMultipleComponent]
public sealed class WalkHistoryUI : MonoBehaviour
{
    private GameObject canvasObject;
    private GameObject panel;
    private RectTransform launcherSafeArea;
    private RectTransform panelSafeArea;
    private RectTransform content;
    private ScrollRect scroll;
    private TMP_Text status;
    private Button refresh;
    private Button more;
    private WalkHistorySession session;
    private bool initializing;
    private string initializationError = "";
    private bool destroyed;
    private int viewGeneration;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;

    private void Start() { BuildView(); }

    private void Update()
    {
        if (canvasObject == null) return;
        UpdateSafeArea();
        if (panel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        if (session != null && session.SynchronizeAccount() && panel.activeSelf)
        {
            Render();
            RefreshHistory();
        }
    }

    public void Open()
    {
        if (canvasObject == null) BuildView();
        panel.SetActive(true);
        RefreshHistory();
    }

    public void Close()
    {
        viewGeneration++;
        initializing = false;
        session?.Reset();
        if (panel != null) panel.SetActive(false);
    }

    private async void RefreshHistory()
    {
        if (initializing || destroyed || !panel.activeSelf) return;
        int revision = ++viewGeneration;
        initializationError = "";
        if (session == null)
        {
            initializing = true;
            Render();
            try
            {
                var manager = StepCountAndGpsManager.Instance;
                var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
                if (bootstrap == null) throw new InvalidOperationException("Walk manager is unavailable.");
                await bootstrap.InitializeAsync();
                if (destroyed || revision != viewGeneration) return;
                if (!bootstrap.IsReady || bootstrap.HistoryStore == null)
                    throw new InvalidOperationException("Firebase is unavailable.");
                session = new WalkHistorySession(bootstrap.HistoryStore);
            }
            catch (Exception)
            {
                if (!destroyed && revision == viewGeneration)
                    initializationError = "Couldn't connect. Check your connection and tap Refresh.";
            }
            finally
            {
                if (revision == viewGeneration) initializing = false;
            }
        }
        if (destroyed || revision != viewGeneration) return;
        if (session == null) { Render(); return; }
        scroll.StopMovement();
        content.anchoredPosition = Vector2.zero;
        Task loading = session.RefreshAsync();
        Render();
        await loading;
        if (!destroyed && revision == viewGeneration) Render();
    }

    private async void LoadMore()
    {
        if (session == null || session.IsLoading) return;
        int revision = viewGeneration;
        Task loading = session.LoadMoreAsync();
        Render();
        await loading;
        if (!destroyed && revision == viewGeneration) Render();
    }

    private void Render()
    {
        if (destroyed || status == null) return;
        // Remove stale rows synchronously, even though Unity destroys them at frame end.
        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        bool loading = initializing || (session != null && session.IsLoading);
        refresh.interactable = !loading;
        more.gameObject.SetActive(session != null && session.HasMore && session.Entries.Count > 0);
        // Also permit paging past a full page consisting entirely of invalid records.
        if (session != null && session.SkippedCount > 0 && session.HasMore) more.gameObject.SetActive(true);
        more.interactable = !loading;

        if (initializing) status.text = "Connecting…";
        else if (!string.IsNullOrEmpty(initializationError)) status.text = initializationError;
        else if (session == null || string.IsNullOrEmpty(session.Owner)) status.text = "Sign in from the title screen to see your walks.";
        else if (loading) status.text = "Loading your walks…";
        else if (!string.IsNullOrEmpty(session.Error)) status.text = session.Error;
        else if (session.Entries.Count == 0) status.text = session.SkippedCount > 0
            ? "Some saved walks couldn't be displayed." : "No uploaded walks yet. Finish a walk and wait for it to sync, then refresh.";
        else status.text = $"{session.Entries.Count} saved walks • Newest first"
            + (session.SkippedCount > 0 ? "\nSome saved walks couldn't be displayed." : "");

        if (session == null) return;
        foreach (var entry in session.Entries)
        {
            RectTransform row = Rect("Walk " + entry.Id, content);
            row.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.20f, 0.24f);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 116;
            layout.preferredHeight = 116;
            TMP_Text date = Label("Date", row, entry.DateLabel, 26);
            Place(date.rectTransform, new Vector2(0, 0.5f), Vector2.one, new Vector2(20, 0), new Vector2(-20, -10));
            TMP_Text stats = Label("Measurements", row, entry.StatsLabel, 23);
            Place(stats.rectTransform, Vector2.zero, new Vector2(1, 0.5f), new Vector2(20, 10), new Vector2(-20, 0));
            stats.color = new Color(0.72f, 0.84f, 0.87f);
        }
    }

    private void BuildView()
    {
        canvasObject = new GameObject("Walk History Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(720, 1280);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        launcherSafeArea = Rect("Safe area", canvas.transform);
        var launch = MakeButton("History", launcherSafeArea, Open);
        Place(launch.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-224, -88), new Vector2(-24, -24));

        RectTransform backdrop = Rect("Walk History", canvas.transform);
        backdrop.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.13f);
        panel = backdrop.gameObject;
        panelSafeArea = Rect("Safe area", backdrop);
        TMP_Text title = Label("Title", panelSafeArea, "Walk history", 36);
        title.fontStyle = FontStyles.Bold;
        Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(24, -100), new Vector2(-196, -24));
        Button close = MakeButton("Back", panelSafeArea, Close);
        Place(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-176, -88), new Vector2(-24, -24));
        status = Label("Status", panelSafeArea, "", 24);
        Place(status.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(24, -208), new Vector2(-24, -108));

        RectTransform viewport = Rect("Walk list", panelSafeArea);
        Place(viewport, Vector2.zero, Vector2.one, new Vector2(24, 112), new Vector2(-24, -224));
        viewport.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.13f);
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;
        content = Rect("Walks", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        var rows = content.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 12;
        rows.childControlHeight = true;
        rows.childControlWidth = true;
        rows.childForceExpandHeight = false;
        rows.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        refresh = MakeButton("Refresh", panelSafeArea, RefreshHistory);
        Place(refresh.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0.5f, 0), new Vector2(24, 24), new Vector2(-12, 88));
        more = MakeButton("Load more", panelSafeArea, LoadMore);
        Place(more.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(12, 24), new Vector2(-24, 88));
        UpdateSafeArea();
        panel.SetActive(false);
    }

    private void UpdateSafeArea()
    {
        var safe = Screen.safeArea;
        var dimensions = new Vector2Int(Screen.width, Screen.height);
        if (dimensions.x <= 0 || dimensions.y <= 0 || (lastSafeArea == safe && lastScreen == dimensions)) return;
        lastSafeArea = safe;
        lastScreen = dimensions;
        foreach (var rect in new[] { launcherSafeArea, panelSafeArea })
        {
            rect.anchorMin = new Vector2(safe.xMin / dimensions.x, safe.yMin / dimensions.y);
            rect.anchorMax = new Vector2(safe.xMax / dimensions.x, safe.yMax / dimensions.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = insetMin;
        rect.offsetMax = insetMax;
    }

    private static TMP_Text Label(string name, Transform parent, string value, float size)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.75f;
        text.fontSizeMax = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.richText = false;
        return text;
    }

    private static Button MakeButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(label, parent);
        var background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.17f, 0.40f, 0.40f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(action);
        TMP_Text text = Label("Label", rect, label, 26);
        Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 6), new Vector2(-10, -6));
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private void OnDisable() { Close(); if (canvasObject != null) canvasObject.SetActive(false); }
    private void OnEnable() { if (canvasObject != null) canvasObject.SetActive(true); }
    private void OnDestroy()
    {
        destroyed = true;
        viewGeneration++;
        session?.Dispose();
        if (canvasObject != null) Destroy(canvasObject);
    }
}
