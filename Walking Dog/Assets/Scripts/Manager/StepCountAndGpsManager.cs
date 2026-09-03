using System.Collections.Generic;
using UnityEngine;

public class StepCountAndGpsManager : MonoBehaviour
{
    public const float AccurateGpsThresholdMeters = 10f;

    private const float EarthRadiusMeters = 6371000f;
    private const float MinRoutePointDistanceMeters = 2f;
    private const float MaxGpsJumpDistanceMeters = 100f;
    private const float MaxWalkingSpeedMetersPerSecond = 8f;

    [Header("Step State")]
    [SerializeField] private int stepsCounted;

    [Header("GPS State")]
    [SerializeField] private float latitude;
    [SerializeField] private float longitude;
    [SerializeField] private float horizontalAccuracy;
    [SerializeField] private bool hasLocation;
    [SerializeField] private string accuracyStatus = "No GPS fix yet.";

    [Header("Walking Session")]
    [SerializeField] private bool walkingSessionActive;
    [SerializeField] private bool hasWalkingSession;
    [SerializeField] private int sessionStartSteps;
    [SerializeField] private int sessionEndSteps;
    [SerializeField] private float sessionStartTime;
    [SerializeField] private float sessionEndTime;
    [SerializeField] private float sessionDistanceMeters;
    [SerializeField] private string sessionStatus = "No walk started.";

    [Header("Route Recording")]
    [SerializeField] private bool recordRoutePoints;
    [SerializeField] private float lastRoutePointTime = -1f;
    [SerializeField] private List<Vector2> routePoints = new List<Vector2>();

    public static StepCountAndGpsManager Instance { get; private set; }

    public int Steps => stepsCounted;
    public float Latitude => latitude;
    public float Longitude => longitude;
    public float HorizontalAccuracy => horizontalAccuracy;
    public bool HasLocation => hasLocation;
    public bool HasAccurateLocation => hasLocation && horizontalAccuracy <= AccurateGpsThresholdMeters;
    public string AccuracyStatus => accuracyStatus;
    public bool IsWalkingSessionActive => walkingSessionActive;
    public bool HasWalkingSession => hasWalkingSession;
    public int WalkingSessionSteps => Mathf.Max(0, (walkingSessionActive ? stepsCounted : sessionEndSteps) - sessionStartSteps);
    public float WalkingSessionDurationSeconds
    {
        get
        {
            if (!hasWalkingSession)
            {
                return 0f;
            }

            var endTime = walkingSessionActive ? Time.realtimeSinceStartup : sessionEndTime;
            return Mathf.Max(0f, endTime - sessionStartTime);
        }
    }

    public float WalkingSessionDistanceMeters => sessionDistanceMeters;
    public string SessionStatus => sessionStatus;
    public bool IsRouteRecording => recordRoutePoints;
    public int RoutePointCount => routePoints.Count;
    public IReadOnlyList<Vector2> RoutePoints => routePoints;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetStep(int steps)
    {
        stepsCounted = Mathf.Max(0, steps);

        if (walkingSessionActive)
        {
            sessionEndSteps = stepsCounted;
        }
    }

    public int GetSteps()
    {
        return stepsCounted;
    }

    public void ResetSteps()
    {
        stepsCounted = 0;

        if (!walkingSessionActive)
        {
            sessionStartSteps = 0;
            sessionEndSteps = 0;
        }
    }

    public void SetGpsLocation(float latitude, float longitude, float accuracyMeters)
    {
        if (!IsUsableGpsFix(latitude, longitude, accuracyMeters))
        {
            accuracyStatus = "Waiting for valid GPS fix.";
            return;
        }

        this.latitude = latitude;
        this.longitude = longitude;
        horizontalAccuracy = Mathf.Max(0f, accuracyMeters);
        hasLocation = true;

        if (HasAccurateLocation)
        {
            accuracyStatus = $"Accurate ({horizontalAccuracy:0.0}m)";

            if (recordRoutePoints)
            {
                AddRoutePoint(latitude, longitude);
            }

            return;
        }

        accuracyStatus = $"Inaccurate ({horizontalAccuracy:0.0}m)";
    }

    public void SetGpsStatus(string status)
    {
        accuracyStatus = string.IsNullOrWhiteSpace(status) ? "No GPS status." : status;
    }

    public void BeginWalkingSession(bool clearExistingRoute = true)
    {
        hasWalkingSession = true;
        walkingSessionActive = true;
        sessionStartSteps = stepsCounted;
        sessionEndSteps = stepsCounted;
        sessionStartTime = Time.realtimeSinceStartup;
        sessionEndTime = sessionStartTime;
        sessionDistanceMeters = 0f;
        sessionStatus = "Walking session active.";

        StartRouteRecording(clearExistingRoute);

        if (HasAccurateLocation)
        {
            AddRoutePoint(latitude, longitude);
        }
    }

    public void EndWalkingSession()
    {
        if (!walkingSessionActive)
        {
            return;
        }

        sessionEndSteps = stepsCounted;
        sessionEndTime = Time.realtimeSinceStartup;
        walkingSessionActive = false;
        recordRoutePoints = false;
        sessionStatus = $"Walk saved: {WalkingSessionSteps} steps, {sessionDistanceMeters:0}m.";
    }

    public void ToggleWalkingSession()
    {
        if (walkingSessionActive)
        {
            EndWalkingSession();
            return;
        }

        BeginWalkingSession();
    }

    public void StartRouteRecording(bool clearExistingRoute = false)
    {
        if (clearExistingRoute)
        {
            ClearRoute();
        }

        recordRoutePoints = true;
    }

    public void StopRouteRecording()
    {
        recordRoutePoints = false;
    }

    public void ClearRoute()
    {
        routePoints.Clear();
        sessionDistanceMeters = 0f;
        lastRoutePointTime = -1f;
    }

    public List<Vector2> GetRoutePointsSnapshot()
    {
        return new List<Vector2>(routePoints);
    }

    private void AddRoutePoint(float latitude, float longitude)
    {
        var point = new Vector2(latitude, longitude);
        var now = Time.realtimeSinceStartup;

        if (routePoints.Count > 0)
        {
            var lastPoint = routePoints[routePoints.Count - 1];
            var distanceFromLastPoint = CalculateDistanceMeters(lastPoint, point);

            if (distanceFromLastPoint < MinRoutePointDistanceMeters)
            {
                return;
            }

            if (IsUnrealisticGpsJump(distanceFromLastPoint, now))
            {
                if (routePoints.Count == 1 && sessionDistanceMeters <= 0f)
                {
                    routePoints[0] = point;
                    lastRoutePointTime = now;
                    sessionStatus = "Route re-anchored after GPS jump.";
                    return;
                }

                sessionStatus = $"Ignored GPS jump ({distanceFromLastPoint:0}m).";
                return;
            }

            sessionDistanceMeters += distanceFromLastPoint;
        }

        routePoints.Add(point);
        lastRoutePointTime = now;
    }

    private bool IsUnrealisticGpsJump(float distanceMeters, float now)
    {
        if (lastRoutePointTime < 0f)
        {
            return false;
        }

        var secondsSinceLastPoint = Mathf.Max(0.001f, now - lastRoutePointTime);
        var allowedDistance = Mathf.Max(MaxGpsJumpDistanceMeters, MaxWalkingSpeedMetersPerSecond * secondsSinceLastPoint);
        return distanceMeters > allowedDistance;
    }

    private static bool IsUsableGpsFix(float latitude, float longitude, float accuracyMeters)
    {
        if (accuracyMeters <= 0f)
        {
            return false;
        }

        if (latitude < -90f || latitude > 90f || longitude < -180f || longitude > 180f)
        {
            return false;
        }

        return !Mathf.Approximately(latitude, 0f) || !Mathf.Approximately(longitude, 0f);
    }

    public static float CalculateDistanceMeters(Vector2 from, Vector2 to)
    {
        return CalculateDistanceMeters(from.x, from.y, to.x, to.y);
    }

    public static float CalculateDistanceMeters(float fromLatitude, float fromLongitude, float toLatitude, float toLongitude)
    {
        var fromLatitudeRadians = fromLatitude * Mathf.Deg2Rad;
        var toLatitudeRadians = toLatitude * Mathf.Deg2Rad;
        var latitudeDelta = (toLatitude - fromLatitude) * Mathf.Deg2Rad;
        var longitudeDelta = (toLongitude - fromLongitude) * Mathf.Deg2Rad;

        var latitudeHaversine = Mathf.Sin(latitudeDelta * 0.5f);
        var longitudeHaversine = Mathf.Sin(longitudeDelta * 0.5f);
        var haversine = (latitudeHaversine * latitudeHaversine) +
            (Mathf.Cos(fromLatitudeRadians) * Mathf.Cos(toLatitudeRadians) * longitudeHaversine * longitudeHaversine);
        haversine = Mathf.Clamp01(haversine);
        var centralAngle = 2f * Mathf.Atan2(Mathf.Sqrt(haversine), Mathf.Sqrt(1f - haversine));

        return EarthRadiusMeters * centralAngle;
    }

    public void setStep(int steps)
    {
        SetStep(steps);
    }

    public int getSteps()
    {
        return GetSteps();
    }

    public void setGPSLonAndLat(float latitude, float longitude, float accuracy)
    {
        SetGpsLocation(latitude, longitude, accuracy);
    }

    public float getLatitude()
    {
        return Latitude;
    }

    public float getLongitude()
    {
        return Longitude;
    }

    public string getAccuracyStatus()
    {
        return AccuracyStatus;
    }

    public float getWalkingSessionDistanceMeters()
    {
        return WalkingSessionDistanceMeters;
    }

    public int getRoutePointCount()
    {
        return RoutePointCount;
    }
}


