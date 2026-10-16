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
    private Color? defaultAccuracyColor;
    private WalkRecoveryUI recoveryUI;

    // Finds UI references and ensures the walk recovery UI component exists.
    private void Awake()
    {
        AssignMissingReferences();
        recoveryUI = GetComponent<WalkRecoveryUI>() ?? gameObject.AddComponent<WalkRecoveryUI>();
    }

    // Connects button click listeners when the UI object becomes active.
    private void OnEnable()
    {
        AssignMissingReferences();
        RegisterButtonListener();
    }

    // Resolves references and wires up button clicks on start.
    private void Start()
    {
        AssignMissingReferences();
        RegisterButtonListener();
    }

    // Detaches button click listeners when the UI object is disabled.
    private void OnDisable()
    {
        UnregisterButtonListener();
    }

    // Refreshes step count, distance, duration, and GPS accuracy status every frame.
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

        // Update the UI with the current step count and GPS status from manager
        long steps = manager.TotalTrackedSteps;

        if (manager.IsWalkingSessionActive)
        {
            SetText(statsText, $"{steps:N0} steps • {FormatDistance(manager.WalkingSessionDistanceMeters)} • {manager.WalkingSessionDurationSeconds / 60:0.0} min");
            SetText(accuracyText, BuildTrackingStatus(manager) + " • " + manager.BackgroundTrackingStatus);
            UpdateTerritoryColor(manager);
            UpdateWalkingSessionButton(manager);
            return;
        }

        if (!manager.HasLocation)
        {
            SetText(statsText, $"{steps:N0} steps");
            SetText(accuracyText, BuildTrackingStatus(manager));
            UpdateTerritoryColor(manager);
            UpdateWalkingSessionButton(manager);
            return;
        }

        SetText(statsText, $"{steps:N0} steps");
        SetText(accuracyText, BuildTrackingStatus(manager));
        UpdateTerritoryColor(manager);

        UpdateWalkingSessionButton(manager);
    }

    // Starts or ends a walking session and presents the summary popup on completion.
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

    // Tints the accuracy text according to the territory capture eligibility flag.
    private void UpdateTerritoryColor(StepCountAndGpsManager manager)
    {
        if (accuracyText == null) return;
        if (!defaultAccuracyColor.HasValue) defaultAccuracyColor = accuracyText.color;
        var flag = manager.IsWalkingSessionActive && string.IsNullOrEmpty(manager.StepTrackingStatus)
            ? manager.getTFlag() : 0;
        accuracyText.color = flag == 2 ? Color.green : flag == 1 ? Color.yellow : defaultAccuracyColor.Value;
    }

    // Finds scene UI elements by expected name if unassigned in inspector.
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

    // Checks whether any required UI element reference is currently missing.
    private bool HasMissingUiReference()
    {
        return statsText == null ||
            accuracyText == null ||
            walkingSessionButton == null ||
            walkingSessionButtonText == null;
    }

    // Hooks up the session button onClick listener once.
    private void RegisterButtonListener()
    {
        if (buttonListenerRegistered || walkingSessionButton == null)
        {
            return;
        }

        walkingSessionButton.onClick.AddListener(ToggleWalkingSession);
        buttonListenerRegistered = true;
    }

    // Removes the session button onClick listener.
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

    // Helper to find a TextMeshProUGUI component on a named game object.
    private static TextMeshProUGUI FindText(string objectName)
    {
        var gameObject = GameObject.Find(objectName);
        return gameObject == null ? null : gameObject.GetComponent<TextMeshProUGUI>();
    }

    // Helper to find a Button component on a named game object.
    private static Button FindButton(string objectName)
    {
        var gameObject = GameObject.Find(objectName);
        return gameObject == null ? null : gameObject.GetComponent<Button>();
    }

    // Updates text content only when changed to minimize canvas and mesh rebuilds.
    private static void SetText(TextMeshProUGUI text, string value)
    {
        // Reassigning TMP text dirties its mesh and the parent Canvas. Avoid doing
        // that on every frame when the displayed value has not changed.
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    // Updates session button label and interactability based on GPS and session state.
    private void UpdateWalkingSessionButton(StepCountAndGpsManager manager)
    {
        if (walkingSessionButton != null)
        {
            walkingSessionButton.interactable = manager != null && (manager.IsWalkingSessionActive || manager.HasFreshLocation);
        }

        if (walkingSessionButtonText != null)
        {
            var label = manager != null && manager.IsWalkingSessionActive
                ? "Finish Territory Walk"
                : manager != null && !manager.HasFreshLocation ? "Waiting for GPS" : "Start Territory Walk";
            if (walkingSessionButtonText.text != label)
            {
                walkingSessionButtonText.text = label;
            }
        }
    }

    // Builds a concise display string summarizing step, GPS, and territory status.
    private static string BuildTrackingStatus(StepCountAndGpsManager manager)
    {
        if (!string.IsNullOrEmpty(manager.StepTrackingStatus)) return manager.StepTrackingStatus;
        var sessionState = manager.IsWalkingSessionActive
            ? "Walking"
            : GetSavedSessionState(manager);

        var tracking = manager.IsWalkingSessionActive ? manager.TrackingStatus
            : !manager.HasFreshLocation && !manager.HasWalkingSession ? manager.AccuracyStatus : sessionState;
        
        // Surface trail progress and loop-closing guidance in the existing status row. It is
        // deliberately a HUD notification, so no Android notification permission is needed.
       
        if (!string.IsNullOrEmpty(manager.TerritoryClosureStatus)) return manager.TerritoryClosureStatus;
        // Keep tracking feedback concise for the dedicated status row.
        
       



        if (!string.IsNullOrEmpty(manager.RecoveryError)) return "Recovery save failed — keep the app open";
        return tracking == "Recording" && manager.HasTrackingGaps ? "Recording • route has gaps" : tracking;
    }

    // Returns a display-ready state string for the most recent saved walk.
    private static string GetSavedSessionState(StepCountAndGpsManager manager)
    {
        if (!manager.HasWalkingSession)
        {
            return "Ready";
        }

        return manager.LastWalkSaveState;
    }

    // Formats raw distance in meters into human-readable meters or kilometers.
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
