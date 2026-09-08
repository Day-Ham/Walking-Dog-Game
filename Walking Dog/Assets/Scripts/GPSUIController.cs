using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GPSUIController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI longText;
    [SerializeField] private TextMeshProUGUI latText;
    [SerializeField] private TextMeshProUGUI accuracyText;
    [SerializeField] private Button walkingSessionButton;
    [SerializeField] private TextMeshProUGUI walkingSessionButtonText;
    [SerializeField] private bool clearRouteOnSessionStart = true;

    private bool buttonListenerRegistered;

    private void Awake()
    {
        AssignMissingReferences();
    }

    private void OnEnable()
    {
        AssignMissingReferences();
        RegisterButtonListener();
    }

    private void Start()
    {
        AssignMissingReferences();
        RegisterButtonListener();
    }

    private void OnDisable()
    {
        UnregisterButtonListener();
    }

    private void Update()
    {
        if (HasMissingUiReference())
        {
            AssignMissingReferences();
            RegisterButtonListener();
        }

        var manager = StepCountAndGpsManager.Instance;

        if (manager == null)
        {
            SetText(longText, "Longitude: --");
            SetText(latText, "Latitude: --");
            SetText(accuracyText, "GPS manager missing");
            UpdateWalkingSessionButton(null);
            return;
        }

        if (!manager.HasLocation)
        {
            SetText(longText, "Longitude: --");
            SetText(latText, "Latitude: --");
            SetText(accuracyText, BuildTrackingStatus(manager));
            UpdateWalkingSessionButton(manager);
            return;
        }

        SetText(longText, $"Longitude: {manager.Longitude:0.000000}");
        SetText(latText, $"Latitude: {manager.Latitude:0.000000}");
        SetText(accuracyText, BuildTrackingStatus(manager));
        UpdateWalkingSessionButton(manager);
    }

    public void ToggleWalkingSession()
    {
        var manager = StepCountAndGpsManager.Instance;

        if (manager == null)
        {
            Debug.LogWarning("Cannot toggle walking session because StepCountAndGpsManager is missing.");
            return;
        }

        if (manager.IsWalkingSessionActive)
        {
            manager.EndWalkingSession();
        }
        else
        {
            manager.BeginWalkingSession(clearRouteOnSessionStart);
        }

        UpdateWalkingSessionButton(manager);
    }

    private void AssignMissingReferences()
    {
        if (longText == null)
        {
            longText = FindText("Longitude");
        }

        if (latText == null)
        {
            latText = FindText("Latitude");
        }

        if (accuracyText == null)
        {
            accuracyText = FindText("Display Accuracy");
        }

        if (walkingSessionButton == null)
        {
            walkingSessionButton = FindButton("Walking Session Button") ?? FindButton("TestButton (1)");
        }

        if (walkingSessionButtonText == null && walkingSessionButton != null)
        {
            walkingSessionButtonText = walkingSessionButton.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    private bool HasMissingUiReference()
    {
        return longText == null ||
            latText == null ||
            accuracyText == null ||
            walkingSessionButton == null ||
            walkingSessionButtonText == null;
    }

    private void RegisterButtonListener()
    {
        if (buttonListenerRegistered || walkingSessionButton == null)
        {
            return;
        }

        walkingSessionButton.onClick.AddListener(ToggleWalkingSession);
        buttonListenerRegistered = true;
    }

    private void UnregisterButtonListener()
    {
        if (!buttonListenerRegistered)
        {
            return;
        }

        if (walkingSessionButton != null)
        {
            walkingSessionButton.onClick.RemoveListener(ToggleWalkingSession);
        }

        buttonListenerRegistered = false;
    }

    private static TextMeshProUGUI FindText(string objectName)
    {
        var gameObject = GameObject.Find(objectName);
        return gameObject == null ? null : gameObject.GetComponent<TextMeshProUGUI>();
    }

    private static Button FindButton(string objectName)
    {
        var gameObject = GameObject.Find(objectName);
        return gameObject == null ? null : gameObject.GetComponent<Button>();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private void UpdateWalkingSessionButton(StepCountAndGpsManager manager)
    {
        if (walkingSessionButton != null)
        {
            walkingSessionButton.interactable = manager != null;
        }

        if (walkingSessionButtonText != null)
        {
            walkingSessionButtonText.text = manager != null && manager.IsWalkingSessionActive
                ? "Stop Walk"
                : "Start Walk";
        }
    }

    private static string BuildTrackingStatus(StepCountAndGpsManager manager)
    {
        var sessionState = manager.IsWalkingSessionActive
            ? "Walking"
            : GetSavedSessionState(manager);

        return $"GPS: {manager.AccuracyStatus} | {sessionState} | {FormatDistance(manager.WalkingSessionDistanceMeters)} | {manager.RoutePointCount} pts";
    }

    private static string GetSavedSessionState(StepCountAndGpsManager manager)
    {
        if (!manager.HasWalkingSession)
        {
            return "Ready";
        }

        return string.IsNullOrEmpty(manager.LastSavedWalkFilePath) ? "Save failed" : "Saved";
    }

    private static string FormatDistance(float meters)
    {
        meters = Mathf.Max(0f, meters);

        if (meters < 1000f)
        {
            return $"{meters:0} m";
        }

        return $"{meters / 1000f:0.00} km";
    }
}
