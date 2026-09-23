using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GPSUIController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private TextMeshProUGUI accuracyText;
    [SerializeField] private Button walkingSessionButton;
    [SerializeField] private TextMeshProUGUI walkingSessionButtonText;
    [SerializeField] private bool clearRouteOnSessionStart = true;

    private bool buttonListenerRegistered;
    private WalkRecoveryUI recoveryUI;

    private void Awake()
    {
        AssignMissingReferences();
        recoveryUI = GetComponent<WalkRecoveryUI>() ?? gameObject.AddComponent<WalkRecoveryUI>();
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
            SetText(statsText, "Steps: --");
            SetText(accuracyText, "GPS manager missing");
            UpdateWalkingSessionButton(null);
            return;
        }

        int steps = manager.Steps;

        if (manager.IsWalkingSessionActive)
        {
            SetText(statsText, $"{manager.WalkingSessionSteps:N0} steps • {FormatDistance(manager.WalkingSessionDistanceMeters)} • {manager.WalkingSessionDurationSeconds / 60:0.0} min");
            SetText(accuracyText, BuildTrackingStatus(manager) + " • " + manager.BackgroundTrackingStatus);
            UpdateWalkingSessionButton(manager);
            return;
        }

        if (!manager.HasLocation)
        {
            SetText(statsText, $"{steps:N0} steps");
            SetText(accuracyText, BuildTrackingStatus(manager));
            UpdateWalkingSessionButton(manager);
            return;
        }

        SetText(statsText, $"{steps:N0} steps");
        SetText(accuracyText, BuildTrackingStatus(manager));
        switch (manager.getTFlag())
        {
            case 1:
                accuracyText.color = Color.yellow;
                break;
            case 2:
                accuracyText.color = Color.green;
                break;
        default:
                break;  
        }

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
            recoveryUI.ShowSummary();
        }
        else
        {
            manager.BeginWalkingSession(clearRouteOnSessionStart);
        }

        UpdateWalkingSessionButton(manager);
    }

    private void AssignMissingReferences()
    {
        if (statsText == null)
        {
            statsText = FindText("Points") ?? FindText("Longitude");
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
        return statsText == null ||
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
            walkingSessionButton.interactable = manager != null && (manager.IsWalkingSessionActive || manager.HasFreshLocation);
        }

        if (walkingSessionButtonText != null)
        {
            walkingSessionButtonText.text = manager != null && manager.IsWalkingSessionActive
                ? "Stop Walk"
                : manager != null && !manager.HasFreshLocation ? "Waiting for GPS" : "Start Walk";
        }
    }

    private static string BuildTrackingStatus(StepCountAndGpsManager manager)
    {
        var sessionState = manager.IsWalkingSessionActive
            ? "Walking"
            : GetSavedSessionState(manager);

        var tracking = manager.IsWalkingSessionActive ? manager.TrackingStatus
            : !manager.HasFreshLocation && !manager.HasWalkingSession ? manager.AccuracyStatus : sessionState;
        
        // Surface the 50 m closure-range notification in the existing status row. It is
        // deliberately a HUD notification, so no Android notification permission is needed.
       
        if (!string.IsNullOrEmpty(manager.TerritoryClosureStatus)) return manager.TerritoryClosureStatus;
        // Keep tracking feedback concise for the dedicated status row.
        
       



        if (!string.IsNullOrEmpty(manager.RecoveryError)) return "Recovery save failed — keep the app open";
        return tracking == "Recording" && manager.HasTrackingGaps ? "Recording • route has gaps" : tracking;
    }

    private static string GetSavedSessionState(StepCountAndGpsManager manager)
    {
        if (!manager.HasWalkingSession)
        {
            return "Ready";
        }

        return manager.LastWalkSaveState;
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
