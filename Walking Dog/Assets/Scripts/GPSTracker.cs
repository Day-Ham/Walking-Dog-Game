using System.Collections;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class GPSTracker : MonoBehaviour
{
    private const int GpsInitializationTimeoutSeconds = 20;
    private const float PermissionRequestTimeoutSeconds = 30f;

    [Header("GPS Settings")]
    [SerializeField] private float desiredAccuracyInMeters = 5f;
    [SerializeField] private float updateInterval = 1.5f;

    private bool gpsRunning = false;
    private Coroutine gpsRoutine;

    public float DesiredAccuracyInMeters => desiredAccuracyInMeters;
    public float UpdateDistance => 0f;
    public float UpdateInterval => updateInterval;
    public bool IsRunning => gpsRunning;

    // Initiates the GPS coroutine when the tracker becomes active.
    private void Start()
    {
        StartGpsRoutine();
    }

    // Restarts GPS polling when the app regains system focus.
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            StartGpsRoutine();
        }
    }

    // Halts GPS polling on background pause and resumes it when unpaused.
    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            if (StepCountAndGpsManager.Instance != null && !StepCountAndGpsManager.Instance.IsBackgroundTracking)
                StepCountAndGpsManager.Instance.InterruptTracking("Tracking interrupted — app paused");
            StopGpsRoutine();
            return;
        }

        StartGpsRoutine();
    }

    // Requests Android fine location permissions, initializes Unity location service, and polls fixes.
    private IEnumerator StartGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            var permissionAnswered = false;
            var permissionGranted = false;
            var callbacks = new PermissionCallbacks();

            callbacks.PermissionGranted += _ =>
            {
                permissionAnswered = true;
                permissionGranted = true;
            };
            callbacks.PermissionDenied += _ =>
            {
                permissionAnswered = true;
                permissionGranted = false;
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                permissionAnswered = true;
                permissionGranted = false;
            };

            Permission.RequestUserPermission(Permission.FineLocation, callbacks);
            SetGpsStatus("Waiting for Android location permission...");

            var permissionWaitSeconds = 0f;
            while (!permissionAnswered && permissionWaitSeconds < PermissionRequestTimeoutSeconds)
            {
                permissionWaitSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!permissionGranted || !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.LogWarning("Location permission was denied. GPS cannot start.");
                SetGpsStatus("Location permission denied.");
                yield break;
            }
        }

        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("GPS is disabled on the device.");
            SetGpsStatus("GPS is disabled on this device.");
            yield break;
        }

        Debug.Log("Starting GPS...");
        SetGpsStatus("Starting GPS...");
        // Keep receiving fresh fixes while stationary; route filtering removes jitter.
        Input.location.Start(desiredAccuracyInMeters, 0f);

        var waitTime = GpsInitializationTimeoutSeconds;

        while (Input.location.status == LocationServiceStatus.Initializing && waitTime > 0)
        {
            Debug.Log("Waiting for GPS...");
            SetGpsStatus("Waiting for GPS fix...");
            yield return new WaitForSeconds(1f);
            waitTime--;
        }

        if (waitTime <= 0)
        {
            Debug.LogWarning("GPS initialization timed out.");
            SetGpsStatus("GPS initialization timed out.");
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("Unable to determine device location.");
            SetGpsStatus("Unable to determine device location.");
            yield break;
        }

        gpsRunning = true;
        Debug.Log("GPS successfully started!");
        SetGpsStatus("GPS started. Waiting for location update...");

        while (gpsRunning)
        {
            UpdateGPSData();
            yield return new WaitForSeconds(updateInterval);
        }
#else
        Debug.Log("GPS Tracker requires a physical Android device.");
        SetGpsStatus("GPS requires a physical Android device.");
        yield break;
#endif
    }

    // Forwards raw Unity location data to StepCountAndGpsManager if GPS is running.
    private void UpdateGPSData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!gpsRunning)
        {
            return;
        }

        var location = Input.location.lastData;
        var manager = StepCountAndGpsManager.Instance;

        Debug.Log($"GPS Location: {location.latitude}, {location.longitude}");

        if (manager == null)
        {
            Debug.LogWarning("StepCountAndGpsManager.Instance is null");
            return;
        }

        if (manager.IsBackgroundTracking) return;
        if (Input.location.status != LocationServiceStatus.Running)
        { manager.InterruptTracking("Tracking interrupted — GPS unavailable"); return; }
        manager.SetGpsLocation(location.latitude, location.longitude, location.horizontalAccuracy,
            location.timestamp, StepCountAndGpsManager.UtcSeconds);

        if (location.horizontalAccuracy > StepCountAndGpsManager.AccurateGpsThresholdMeters)
        {
            Debug.LogWarning("GPS reading is inaccurate; location displayed but route point not recorded.");
        }
#endif
    }

    // Stops GPS updates when this component is disabled.
    private void OnDisable()
    {
        StopGpsRoutine();
    }

    // Stops GPS updates when this component is destroyed.
    private void OnDestroy()
    {
        StopGpsRoutine();
    }

    // Launches the GPS runner coroutine if not already running.
    private void StartGpsRoutine()
    {
        if (gpsRoutine != null)
        {
            return;
        }

        gpsRoutine = StartCoroutine(RunGPS());
    }

    // Coroutine lifecycle wrapper that runs StartGPS and handles cleanup.
    private IEnumerator RunGPS()
    {
        // Yield first so the coroutine handle is assigned even on immediate failure.
        yield return null;
        yield return StartGPS();
        StopGPS();
        gpsRoutine = null;
    }

    // Stops the GPS polling coroutine and disables hardware location updates.
    private void StopGpsRoutine()
    {
        if (gpsRoutine != null)
        {
            StopCoroutine(gpsRoutine);
            gpsRoutine = null;
        }

        StopGPS();
    }

    // Stops the Android native location service.
    private void StopGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Input.location.Stop();
        gpsRunning = false;
#endif
    }

    // Updates the GPS status message on StepCountAndGpsManager.
    private static void SetGpsStatus(string status)
    {
        var manager = StepCountAndGpsManager.Instance;
        if (manager != null)
        {
            manager.SetGpsStatus(status);
        }
    }
}
