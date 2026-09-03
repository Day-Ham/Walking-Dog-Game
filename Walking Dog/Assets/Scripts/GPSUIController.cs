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

    private void Awake()
    {
        AssignMissingReferences();
    }

    private void OnEnable()
    {
        AssignMissingReferences();

        if (walkingSessionButton != null)
        {
            walkingSessionButton.onClick.AddListener(ToggleWalkingSession);
        }
    }

    private void OnDisable()
    {
        if (walkingSessionButton != null)
        {
            walkingSessionButton.onClick.RemoveListener(ToggleWalkingSession);
        }
    }

    private void Update()
    {
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
            : manager.HasWalkingSession ? "Stopped" : "Ready";

        return $"GPS: {manager.AccuracyStatus} | {sessionState} | {FormatDistance(manager.WalkingSessionDistanceMeters)} | {manager.RoutePointCount} pts";
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
