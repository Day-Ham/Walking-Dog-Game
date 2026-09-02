using UnityEngine;
using System.Collections;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class GPSTracker : MonoBehaviour
{
    [Tooltip("Minimum distance in meters before registering a movement (reduces drift).")]
    public float minUpdateDistance = 2.0f;
    
    [Tooltip("Reference to the StepCounterDisplay to check if the user is actively walking.")]
    public StepCounterDisplay stepCounter;

    public float TotalDistanceMeters { get; private set; }
    public bool IsLocationServiceRunning { get; private set; }
    public string GPSStatus { get; private set; } = "Initializing...";

    private LocationInfo lastData;
    private bool hasValidLastData = false;
    private bool hasPermission = false;

    private void Start()
    {
        if (stepCounter == null)
        {
            stepCounter = FindObjectOfType<StepCounterDisplay>();
        }

        StartCoroutine(InitializeGPS());
    }

    private IEnumerator InitializeGPS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            GPSStatus = "Requesting Location Permission...";
            
            // Wait a bit for the user to respond to the dialog
            yield return new WaitForSeconds(2f);
            
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                yield return new WaitForSeconds(1f);
            }
        }
#endif
        hasPermission = true;

        if (!Input.location.isEnabledByUser)
        {
            GPSStatus = "Location services disabled by user.";
            yield break;
        }

        // Start service before querying location
        GPSStatus = "Starting location services...";
        Input.location.Start(1f, 1f); // 1 meter accuracy, 1 meter update distance

        // Wait until service initializes
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait < 1)
        {
            GPSStatus = "Timed out waiting for location services.";
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            GPSStatus = "Unable to determine device location.";
            yield break;
        }

        GPSStatus = "GPS Connected.";
        IsLocationServiceRunning = true;
    }

    private void Update()
    {
        if (!IsLocationServiceRunning || Input.location.status != LocationServiceStatus.Running) return;

        bool isWalking = stepCounter != null && stepCounter.IsActivelyWalking;
        
        LocationInfo currentData = Input.location.lastData;

        // If we don't have a valid last position yet, initialize it.
        // We do this even if not walking so we get a starting point.
        if (!hasValidLastData)
        {
            lastData = currentData;
            hasValidLastData = true;
            return;
        }

        // Only update location and distance if the step tracker says we are walking.
        // This prevents GPS drift while standing still and stops cars from adding distance.
        if (isWalking)
        {
            float distanceDelta = CalculateDistance(lastData.latitude, lastData.longitude, currentData.latitude, currentData.longitude);

            if (distanceDelta >= minUpdateDistance)
            {
                TotalDistanceMeters += distanceDelta;
                lastData = currentData;
                GPSStatus = $"Tracking... Distance: {TotalDistanceMeters:0.0}m";
            }
            else
            {
                GPSStatus = $"Walking... (Delta too small: {distanceDelta:0.0}m)";
            }
        }
        else
        {
            GPSStatus = "Waiting for steps to update GPS...";
            // If they are not walking, we intentionally DO NOT update lastData to currentData.
            // When they start walking again, the distance will be calculated from the last known 
            // "walking" position, preventing teleportation jumps from drift during standstill.
        }
    }

    // Calculates distance in meters between two lat/long points using the Haversine formula
    private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
    {
        float R = 6371000; // Radius of earth in meters
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        
        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad) *
                  Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
                  
        float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
        return R * c;
    }

    private void OnDisable()
    {
        if (IsLocationServiceRunning)
        {
            Input.location.Stop();
            IsLocationServiceRunning = false;
        }
    }
}
