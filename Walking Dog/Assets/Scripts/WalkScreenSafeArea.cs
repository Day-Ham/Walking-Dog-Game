using UnityEngine;

// Keep the HUD and native map inside the same usable phone rectangle.
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(RectTransform))]
public sealed class WalkScreenSafeArea : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;

    private void OnEnable()
    {
        rectTransform = GetComponent<RectTransform>();
        lastScreen = Vector2Int.zero;
        Update();
    }

    private void Update()
    {
        var dimensions = new Vector2Int(Screen.width, Screen.height);
        var safe = Screen.safeArea;
        if (dimensions.x <= 0 || dimensions.y <= 0 || safe.width <= 0 || safe.height <= 0)
            return;
        if (lastScreen == dimensions && lastSafeArea == safe) return;

        lastScreen = dimensions;
        lastSafeArea = safe;
        rectTransform.anchorMin = new Vector2(safe.xMin / dimensions.x, safe.yMin / dimensions.y);
        rectTransform.anchorMax = new Vector2(safe.xMax / dimensions.x, safe.yMax / dimensions.y);
        rectTransform.offsetMin = rectTransform.offsetMax = Vector2.zero;
        // The Android WebView reads world corners when it updates its bounds.
        Canvas.ForceUpdateCanvases();
    }
}
