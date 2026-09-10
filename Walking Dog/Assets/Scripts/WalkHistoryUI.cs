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
    [SerializeField]
    private GameObject panel;
    [SerializeField]
    private RectTransform content;
    [SerializeField]
    private ScrollRect scroll;
    [SerializeField]
    private TMP_Text status;
    [SerializeField]
    private Button refresh;
    [SerializeField]
    private Button more;

    [SerializeField]
    private GameObject mapPanel;// 
    [SerializeField]
    private OpenFreeMapWebViewMap openFreeMap; // keep for map
    [SerializeField]
    private TMP_Text mapStatus; // keep for map status messages

    [SerializeField]
    private WalkDataTab walkDataTabPrefab; // keep for row prefab

    private WalkHistorySession session; // keep
    private bool initializing; // keep for status
    private string initializationError = ""; // keep for error message
    private bool destroyed; // for destroying
 
    private int viewGeneration; //keep

   

    private void Start() { }

    // Unity calls this every frame; updates safe-area layout, handles Escape, and refreshes after an account change.
    private void Update()
    {
       
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (mapPanel != null && mapPanel.activeSelf) CloseMap();
            else if (panel.activeSelf) Close();
        }
        if (session != null && session.SynchronizeAccount() && panel.activeSelf)
        {
            Render();
            RefreshHistory();
        }
    }

    private System.Collections.Generic.List<OpenFreeMapWebViewMap> otherMaps = new System.Collections.Generic.List<OpenFreeMapWebViewMap>(); // keep 

    // Opens the history panel, starts a Firestore refresh, and temporarily disables other active map components.
    public void Open()
    {
        panel.SetActive(true);
        RefreshHistory();
        
        otherMaps.Clear();
        foreach (var map in FindObjectsOfType<OpenFreeMapWebViewMap>())
        {
            if (map != openFreeMap && map.enabled)
            {
                map.enabled = false;
                otherMaps.Add(map);
            }
        }
    }

    // Closes history and map panels, invalidates pending loads, resets the session, and restores other maps.
    public void Close()
    {
        viewGeneration++;
        initializing = false;
        session?.Reset();
        if (mapPanel != null) mapPanel.SetActive(false);
        if (panel != null) panel.SetActive(false);
        
        foreach (var map in otherMaps)
        {
            if (map != null) map.enabled = true;
        }
        otherMaps.Clear();
    }

    // Hides only the historical-route map panel.
    public void CloseMap()
    {
        if (mapPanel != null) mapPanel.SetActive(false);
    }

    // Initializes Firebase/history access if needed, then requests the first page of Firestore history entries.
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

    // Requests the next page of Firestore history entries when the Load more button is clicked.
    private async void LoadMore()
    {
        if (session == null || session.IsLoading) return;
        int revision = viewGeneration;
        Task loading = session.LoadMoreAsync();
        Render();
        await loading;
        if (!destroyed && revision == viewGeneration) Render();
    }

    // Rebuilds the visible status and walk-row UI from the current history session state.
    private void Render()
    {
        if (destroyed || status == null) return;
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
        foreach (Transform child in content)
        {
            // Keep the inactive template; delete only rows created from it.
            if (walkDataTabPrefab != null && child == walkDataTabPrefab.transform)
                continue;

            Destroy(child.gameObject);
        }

        if (session == null)
            return;

        foreach (var entry in session.Entries)
        {
            RectTransform row = Rect("Walk " + entry.Id, content);
            row.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.20f, 0.24f);
            var button = row.gameObject.AddComponent<Button>();
            string entryId = entry.Id;
            button.onClick.AddListener(() => OpenWalkMap(entryId));

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


    // Opens a selected walk's map using matching local GPS route points; Firestore history contains only the list summary.
    private void OpenWalkMap(string entryId)
    {
        if (StepCountAndGpsManager.Instance == null) return;
        var localWalks = StepCountAndGpsManager.Instance.LocalWalks;
        var walk = localWalks.Find(entryId);
        
        if (walk != null && walk.routePoints != null && walk.routePoints.Count > 0)
        {
            var points = new System.Collections.Generic.List<Vector2>();
            foreach (var point in walk.routePoints)
            {
                points.Add(new Vector2(point.latitude, point.longitude));
            }
            openFreeMap.HistoricalRoutePoints = points;
            openFreeMap.ShowHistoricalRoute = true;
            openFreeMap.enabled = true;
            mapStatus.text = "";
        }
        else
        {
            openFreeMap.HistoricalRoutePoints = null;
            openFreeMap.ShowHistoricalRoute = false;
            openFreeMap.enabled = false;
            
            if (walk == null)
            {
                mapStatus.text = "Route data is only available on the device used to record the walk.";
            }
            else
            {
                mapStatus.text = "No GPS route was recorded for this walk.";
            }
        }
        
        mapPanel.SetActive(true);
    }

    private void OnDisable()
    {
        Close();
    }

    private void OnDestroy()
    {
        destroyed = true;
        viewGeneration++;
        session?.Dispose();
    }

    public void OnRefreshClicked()
    {
        RefreshHistory();
    }

    public void OnLoadMoreClicked()
    {
        LoadMore();
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return rect;
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
    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = insetMin;
        rect.offsetMax = insetMax;
    }

}
