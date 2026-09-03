using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class OpenFreeMapWebViewMap : MonoBehaviour
{
    private const string DefaultHtmlFileName = "OpenFreeMapMap.html";
    private const int ViewVisible = 0;
    private const int ViewGone = 8;

    public enum OpenFreeMapStyle
    {
        Liberty,
        Positron,
        Bright,
        Dark,
        Fiord
    }

    [Header("Map")]
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private OpenFreeMapStyle mapStyle = OpenFreeMapStyle.Liberty;
    [SerializeField, Range(1, 20)] private int zoom = 17;
    [SerializeField] private bool followGps = true;
    [SerializeField] private bool allowMapGestures = true;
    [SerializeField] private Vector2 fallbackLatitudeLongitude = new Vector2(14.5995f, 120.9842f);

#pragma warning disable 0414
    [Header("Sync")]
    [SerializeField] private float mapSyncIntervalSeconds = 1f;
    [SerializeField] private float layoutSyncIntervalSeconds = 0.5f;
    [SerializeField] private int maxRoutePointsToSend = 700;
    [SerializeField] private string htmlFileName = DefaultHtmlFileName;

    [Header("Optional UI")]
    [SerializeField] private TextMeshProUGUI statusText;

    private readonly Vector3[] mapCorners = new Vector3[4];
    private float nextMapSyncTime;
    private float nextLayoutSyncTime;
#pragma warning restore 0414
    private RectInt lastAndroidRect;
    private bool hasLastAndroidRect;
    private bool webViewVisible;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject activity;
    private AndroidJavaObject webView;
    private AndroidJavaObject layoutParams;
#endif

    private void Awake()
    {
        if (mapArea == null)
        {
            mapArea = GetComponent<RectTransform>();
        }
    }

    private void OnEnable()
    {
        nextMapSyncTime = 0f;
        nextLayoutSyncTime = 0f;
        SetStatus("Starting OpenFreeMap...");
        CreateOrShowWebView();
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        CreateOrShowWebView();

        if (Time.unscaledTime >= nextLayoutSyncTime)
        {
            nextLayoutSyncTime = Time.unscaledTime + Mathf.Max(0.1f, layoutSyncIntervalSeconds);
            UpdateWebViewLayoutIfNeeded();
        }

        if (Time.unscaledTime >= nextMapSyncTime)
        {
            nextMapSyncTime = Time.unscaledTime + Mathf.Max(0.1f, mapSyncIntervalSeconds);
            SyncMapStateToWebView();
        }
#else
        SetStatus("OpenFreeMap preview appears in Android builds.");
#endif
    }

    private void OnDisable()
    {
        SetWebViewVisible(false);
    }

    private void OnDestroy()
    {
        DestroyWebView();
    }

    private void OnApplicationPause(bool isPaused)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        RunOnAndroidUiThread(() =>
        {
            if (webView == null)
            {
                return;
            }

            webView.Call(isPaused ? "onPause" : "onResume");
        });
#endif
    }

    public void ForceSync()
    {
        nextMapSyncTime = 0f;
        nextLayoutSyncTime = 0f;
    }

    public void SetZoom(int newZoom)
    {
        zoom = Mathf.Clamp(newZoom, 1, 20);
        ForceSync();
    }

    private void CreateOrShowWebView()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (webView != null)
        {
            if (!webViewVisible)
            {
                SetWebViewVisible(true);
            }

            return;
        }

        activity = GetUnityActivity();
        if (activity == null)
        {
            SetStatus("Waiting for Android activity...");
            return;
        }

        var rect = CalculateAndroidRect();
        var safeHtmlFileName = GetSafeHtmlFileName();
        SetStatus("Loading OpenFreeMap...");

        RunOnAndroidUiThread(() =>
        {
            if (webView != null)
            {
                SetWebViewVisibleOnUiThread(true);
                return;
            }

            EnableWebViewDebuggingForDevelopmentBuilds();

            webView = new AndroidJavaObject("android.webkit.WebView", activity);
            ConfigureWebView(webView);

            layoutParams = CreateLayoutParams(rect);
            lastAndroidRect = rect;
            hasLastAndroidRect = true;

            activity.Call("addContentView", webView, layoutParams);
            webView.Call("bringToFront");
            webView.Call("loadUrl", $"file:///android_asset/{safeHtmlFileName}");
            webViewVisible = true;
        });
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject GetUnityActivity()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Unable to access Unity Android activity: " + exception.Message);
            return null;
        }
    }

    private void ConfigureWebView(AndroidJavaObject targetWebView)
    {
        var settings = targetWebView.Call<AndroidJavaObject>("getSettings");
        settings.Call("setJavaScriptEnabled", true);
        settings.Call("setDomStorageEnabled", true);
        settings.Call("setLoadWithOverviewMode", true);
        settings.Call("setUseWideViewPort", true);
        settings.Call("setAllowFileAccess", true);
        settings.Call("setAllowContentAccess", true);
        settings.Call("setAllowFileAccessFromFileURLs", true);
        settings.Call("setAllowUniversalAccessFromFileURLs", true);

        try
        {
            settings.Call("setMixedContentMode", 0);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("OpenFreeMap WebView mixed content setting was not applied: " + exception.Message);
        }

        settings.Dispose();

        using (var webViewClient = new AndroidJavaObject("android.webkit.WebViewClient"))
        {
            targetWebView.Call("setWebViewClient", webViewClient);
        }

        using (var chromeClient = new AndroidJavaObject("android.webkit.WebChromeClient"))
        {
            targetWebView.Call("setWebChromeClient", chromeClient);
        }

        targetWebView.Call("setVerticalScrollBarEnabled", false);
        targetWebView.Call("setHorizontalScrollBarEnabled", false);
        targetWebView.Call("setClickable", allowMapGestures);

        try
        {
            targetWebView.Call("setLayerType", 2, (object)null);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("OpenFreeMap WebView hardware layer was not applied: " + exception.Message);
        }
    }

    private void EnableWebViewDebuggingForDevelopmentBuilds()
    {
        if (!Debug.isDebugBuild)
        {
            return;
        }

        try
        {
            using (var webViewClass = new AndroidJavaClass("android.webkit.WebView"))
            {
                webViewClass.CallStatic("setWebContentsDebuggingEnabled", true);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("OpenFreeMap WebView debugging was not enabled: " + exception.Message);
        }
    }

    private AndroidJavaObject CreateLayoutParams(RectInt rect)
    {
        var parameters = new AndroidJavaObject(
            "android.widget.FrameLayout$LayoutParams",
            Mathf.Max(1, rect.width),
            Mathf.Max(1, rect.height));

        using (var gravity = new AndroidJavaClass("android.view.Gravity"))
        {
            parameters.Set("gravity", gravity.GetStatic<int>("TOP") | gravity.GetStatic<int>("START"));
        }

        parameters.Set("leftMargin", Mathf.Max(0, rect.x));
        parameters.Set("topMargin", Mathf.Max(0, rect.y));
        return parameters;
    }

    private void UpdateWebViewLayoutIfNeeded()
    {
        if (webView == null || layoutParams == null)
        {
            return;
        }

        var rect = CalculateAndroidRect();
        if (hasLastAndroidRect && rect == lastAndroidRect)
        {
            return;
        }

        lastAndroidRect = rect;
        hasLastAndroidRect = true;

        RunOnAndroidUiThread(() =>
        {
            if (webView == null || layoutParams == null)
            {
                return;
            }

            layoutParams.Set("width", Mathf.Max(1, rect.width));
            layoutParams.Set("height", Mathf.Max(1, rect.height));
            layoutParams.Set("leftMargin", Mathf.Max(0, rect.x));
            layoutParams.Set("topMargin", Mathf.Max(0, rect.y));
            webView.Call("setLayoutParams", layoutParams);
        });
    }

    private void SyncMapStateToWebView()
    {
        if (webView == null)
        {
            return;
        }

        var state = BuildMapState();
        var json = JsonUtility.ToJson(state);
        EvaluateJavaScript($"window.updateDogWalkState && window.updateDogWalkState({json});");
        SetStatus(state.hasLocation ? "OpenFreeMap tracking GPS." : "OpenFreeMap preview location.");
    }

    private void EvaluateJavaScript(string script)
    {
        RunOnAndroidUiThread(() =>
        {
            if (webView == null)
            {
                return;
            }

            webView.Call("evaluateJavascript", script, null);
        });
    }

    private void SetWebViewVisible(bool isVisible)
    {
        RunOnAndroidUiThread(() => SetWebViewVisibleOnUiThread(isVisible));
    }

    private void SetWebViewVisibleOnUiThread(bool isVisible)
    {
        if (webView == null)
        {
            webViewVisible = false;
            return;
        }

        webView.Call("setVisibility", isVisible ? ViewVisible : ViewGone);
        webViewVisible = isVisible;
    }

    private void DestroyWebView()
    {
        if (webView == null)
        {
            return;
        }

        var webViewToDestroy = webView;
        webView = null;
        layoutParams = null;
        webViewVisible = false;

        RunOnAndroidUiThread(() =>
        {
            try
            {
                var parent = webViewToDestroy.Call<AndroidJavaObject>("getParent");
                if (parent != null)
                {
                    parent.Call("removeView", webViewToDestroy);
                    parent.Dispose();
                }

                webViewToDestroy.Call("stopLoading");
                webViewToDestroy.Call("destroy");
                webViewToDestroy.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Failed to destroy OpenFreeMap WebView: " + exception.Message);
            }
        });
    }

    private void RunOnAndroidUiThread(Action action)
    {
        if (activity == null)
        {
            activity = GetUnityActivity();
        }

        if (activity == null || action == null)
        {
            return;
        }

        activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
    }
#else
    private void SetWebViewVisible(bool isVisible)
    {
    }

    private void DestroyWebView()
    {
    }
#endif

    private OpenFreeMapState BuildMapState()
    {
        var manager = StepCountAndGpsManager.Instance;
        var hasGpsLocation = manager != null && manager.HasLocation;
        var latitude = hasGpsLocation ? manager.Latitude : fallbackLatitudeLongitude.x;
        var longitude = hasGpsLocation ? manager.Longitude : fallbackLatitudeLongitude.y;

        var state = new OpenFreeMapState
        {
            hasLocation = hasGpsLocation,
            follow = followGps,
            allowGestures = allowMapGestures,
            lat = latitude,
            lng = longitude,
            zoom = zoom,
            style = GetStyleId(mapStyle),
            routePoints = new List<OpenFreeMapRoutePoint>()
        };

        if (manager != null && manager.RoutePointCount > 0)
        {
            AddRoutePoints(manager.RoutePoints, state.routePoints);
        }

        return state;
    }

    private void AddRoutePoints(IReadOnlyList<Vector2> sourceRoutePoints, List<OpenFreeMapRoutePoint> destination)
    {
        if (sourceRoutePoints == null || sourceRoutePoints.Count == 0)
        {
            return;
        }

        var maxPoints = Mathf.Max(2, maxRoutePointsToSend);
        var stride = Mathf.Max(1, Mathf.CeilToInt(sourceRoutePoints.Count / (float)maxPoints));

        for (var i = 0; i < sourceRoutePoints.Count; i += stride)
        {
            destination.Add(ToRoutePoint(sourceRoutePoints[i]));
        }

        var lastPoint = sourceRoutePoints[sourceRoutePoints.Count - 1];
        if (destination.Count == 0 ||
            !Mathf.Approximately(destination[destination.Count - 1].lat, lastPoint.x) ||
            !Mathf.Approximately(destination[destination.Count - 1].lng, lastPoint.y))
        {
            destination.Add(ToRoutePoint(lastPoint));
        }
    }

    private RectInt CalculateAndroidRect()
    {
        if (mapArea == null)
        {
            mapArea = GetComponent<RectTransform>();
        }

        if (mapArea == null)
        {
            return new RectInt(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        }

        mapArea.GetWorldCorners(mapCorners);

        var canvas = mapArea.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var i = 0; i < mapCorners.Length; i++)
        {
            var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, mapCorners[i]);
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        minX = Mathf.Clamp(minX, 0f, Screen.width);
        maxX = Mathf.Clamp(maxX, 0f, Screen.width);
        minY = Mathf.Clamp(minY, 0f, Screen.height);
        maxY = Mathf.Clamp(maxY, 0f, Screen.height);

        var left = Mathf.RoundToInt(minX);
        var top = Mathf.RoundToInt(Screen.height - maxY);
        var width = Mathf.RoundToInt(Mathf.Max(1f, maxX - minX));
        var height = Mathf.RoundToInt(Mathf.Max(1f, maxY - minY));

        return new RectInt(left, top, width, height);
    }

    private string GetSafeHtmlFileName()
    {
        return string.IsNullOrWhiteSpace(htmlFileName) ? DefaultHtmlFileName : htmlFileName;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private static OpenFreeMapRoutePoint ToRoutePoint(Vector2 latitudeLongitude)
    {
        return new OpenFreeMapRoutePoint
        {
            lat = latitudeLongitude.x,
            lng = latitudeLongitude.y
        };
    }

    private static string GetStyleId(OpenFreeMapStyle style)
    {
        switch (style)
        {
            case OpenFreeMapStyle.Positron:
                return "positron";
            case OpenFreeMapStyle.Bright:
                return "bright";
            case OpenFreeMapStyle.Dark:
                return "dark";
            case OpenFreeMapStyle.Fiord:
                return "fiord";
            case OpenFreeMapStyle.Liberty:
            default:
                return "liberty";
        }
    }

    [Serializable]
    private class OpenFreeMapState
    {
        public bool hasLocation;
        public bool follow;
        public bool allowGestures;
        public float lat;
        public float lng;
        public int zoom;
        public string style;
        public List<OpenFreeMapRoutePoint> routePoints;
    }

    [Serializable]
    private class OpenFreeMapRoutePoint
    {
        public float lat;
        public float lng;
    }
}
