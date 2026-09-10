using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Added by the existing walk controls; no scene wiring is required.
[DisallowMultipleComponent]
public sealed class WalkRecoveryUI : MonoBehaviour
{
    private GameObject canvasObject;
    private RectTransform safeArea;
    private TMP_Text title, details;
    private Button primary, secondary;
    private readonly List<OpenFreeMapWebViewMap> hiddenMaps = new List<OpenFreeMapWebViewMap>();
    private StepCountAndGpsManager.SavedWalkSession pending;
    private float nextCheck;
    private bool summary;

    private void Update()
    {
        var manager = StepCountAndGpsManager.Instance;
        if (manager == null) return;
        if (Time.unscaledTime >= nextCheck && !manager.IsWalkingSessionActive && !summary)
        {
            nextCheck = Time.unscaledTime + 1f;
            if (manager.HasUnsavedCompletedWalk) { ShowSummary(); return; }
            var candidate = manager.RecoverableWalk;
            if (candidate != null && (pending == null || pending.id != candidate.id))
            { pending = candidate; summary = false; Show(); }
            else if (candidate == null && pending != null && !summary) Close();
        }
        if (canvasObject == null || !canvasObject.activeSelf) return;
        var safe = Screen.safeArea;
        safeArea.anchorMin = new Vector2(safe.xMin / Mathf.Max(1, Screen.width), safe.yMin / Mathf.Max(1, Screen.height));
        safeArea.anchorMax = new Vector2(safe.xMax / Mathf.Max(1, Screen.width), safe.yMax / Mathf.Max(1, Screen.height));
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
    }

    public void ShowSummary()
    {
        pending = null;
        summary = true;
        Show();
    }

    private void Show()
    {
        if (canvasObject == null) Build();
        if (!canvasObject.activeSelf)
        {
            hiddenMaps.Clear();
            // Android WebViews sit above Unity canvases; hide them while a dialog is open.
            foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>())
                if (map.enabled) { hiddenMaps.Add(map); map.enabled = false; }
        }
        canvasObject.SetActive(true);
        var manager = StepCountAndGpsManager.Instance;
        title.text = summary ? "Walk finished" : "Unfinished walk found";
        details.text = summary
            ? $"{manager.WalkingSessionSteps:N0} steps  •  {manager.WalkingSessionDistanceMeters:0} m\n{manager.WalkingSessionDurationSeconds / 60f:0.0} minutes\n\n{manager.LastWalkSaveState}\n\n" +
              (manager.HasTrackingGaps ? "Parts of this route are missing. Gaps are shown as breaks on the map." :
               manager.RoutePointCount < 2 ? "There are not enough GPS points to show a route." : "Your recorded route is ready to review on the map.")
            : $"{pending.steps:N0} steps  •  {pending.distanceMeters:0} m\nSaved {LocalWalkRepository.ParseUtc(pending.endedAtUtc).ToLocalTime():MMM d, h:mm tt}\n\nResume this walk, or save what was recorded and finish. Any missing tracking will remain a break in the route.";
        primary.GetComponentInChildren<TMP_Text>().text = summary ? (manager.RoutePointCount > 1 ? "View map" : "Done") : "Resume walk";
        primary.interactable = !summary || !manager.HasUnsavedCompletedWalk;
        secondary.GetComponentInChildren<TMP_Text>().text = summary ? "Retry save" : "Save and finish";
        secondary.gameObject.SetActive(!summary || string.IsNullOrEmpty(manager.LastSavedWalkFilePath));
    }

    private void Primary()
    {
        if (summary) { Close(); return; }
        if (StepCountAndGpsManager.Instance.RecoverWalk(true)) Close();
    }

    private void Secondary()
    {
        var manager = StepCountAndGpsManager.Instance;
        if (summary) { manager.SaveCurrentWalkingSession(); Show(); }
        else if (manager.RecoverWalk(false)) ShowSummary();
    }

    private void Close()
    {
        pending = null;
        summary = false;
        if (canvasObject != null) canvasObject.SetActive(false);
        foreach (var map in hiddenMaps) if (map != null && map.gameObject.activeInHierarchy) map.enabled = true;
        hiddenMaps.Clear();
    }

    private void OnDestroy() { Close(); if (canvasObject != null) Destroy(canvasObject); }

    private void Build()
    {
        canvasObject = new GameObject("Walk Recovery Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var scale = canvasObject.GetComponent<CanvasScaler>();
        scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scale.referenceResolution = new Vector2(720, 1280);
        scale.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var background = Rect("Background", canvas.transform);
        background.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.06f, 0.08f, 0.97f);
        safeArea = Rect("Safe area", canvas.transform);
        var card = Rect("Walk details", safeArea);
        card.offsetMin = new Vector2(32, 32);
        card.offsetMax = new Vector2(-32, -32);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        title = Label("Title", card, 34, 70);
        title.fontStyle = FontStyles.Bold;
        details = Label("Details", card, 25, 310);
        primary = MakeButton("Resume walk", card, Primary);
        secondary = MakeButton("Save and finish", card, Secondary);
        canvasObject.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static TMP_Text Label(string name, Transform parent, float size, float height)
    {
        var rect = Rect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return text;
    }
    private static Button MakeButton(string text, Transform parent, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(text, parent);
        rect.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.38f, 0.40f);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
        var button = rect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(action);
        var label = Label("Label", rect, 26, 80);
        label.text = text;
        return button;
    }
}
