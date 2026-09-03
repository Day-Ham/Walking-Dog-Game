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
    [SerializeField] private float updateDistance = 3f;
    [SerializeField] private float updateInterval = 1.5f;

    private bool gpsRunning = false;

    public float DesiredAccuracyInMeters => desiredAccuracyInMeters;
    public float UpdateDistance => updateDistance;
    public float UpdateInterval => updateInterval;
    public bool IsRunning => gpsRunning;

    private void Start()
    {
        StartCoroutine(StartGPS());
    }

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
        Input.location.Start(desiredAccuracyInMeters, updateDistance);

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

        manager.SetGpsLocation(location.latitude, location.longitude, location.horizontalAccuracy);

        if (location.horizontalAccuracy > StepCountAndGpsManager.AccurateGpsThresholdMeters)
        {
            Debug.LogWarning("GPS reading is inaccurate; location displayed but route point not recorded.");
        }
#endif
    }

    private void OnDisable()
    {
        StopGPS();
    }

    private void OnDestroy()
    {
        StopGPS();
    }

    private void StopGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (gpsRunning)
        {
            Input.location.Stop();
            gpsRunning = false;
            Debug.Log("GPS stopped.");
            SetGpsStatus("GPS stopped.");
        }
#endif
    }

    private static void SetGpsStatus(string status)
    {
        var manager = StepCountAndGpsManager.Instance;
        if (manager != null)
        {
            manager.SetGpsStatus(status);
        }
    }
}
