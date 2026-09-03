using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class StepCountAndGpsManager : MonoBehaviour, ISerializationCallbackReceiver
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
    [SerializeField] private string currentSessionId = "";
    [SerializeField] private string sessionStartUtc = "";
    [SerializeField] private string sessionEndUtc = "";
    [SerializeField] private string lastSavedWalkFilePath = "";

    [Header("Route Recording")]
    [SerializeField] private bool recordRoutePoints;
    [SerializeField] private float lastRoutePointTime = -1f;
    [SerializeField] private List<Vector2> routePoints = new List<Vector2>();
    [SerializeField] private List<WalkRoutePoint> routePointSamples = new List<WalkRoutePoint>();

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
    public string CurrentSessionId => currentSessionId;
    public string SessionStartUtc => sessionStartUtc;
    public string SessionEndUtc => sessionEndUtc;
    public string LastSavedWalkFilePath => lastSavedWalkFilePath;
    public string SavedWalkDirectoryPath => Path.Combine(Application.persistentDataPath, "WalkSessions");
    public bool IsRouteRecording => recordRoutePoints;
    public int RoutePointCount
    {
        get
        {
            EnsureCollections();
            return routePoints.Count;
        }
    }

    public IReadOnlyList<Vector2> RoutePoints
    {
        get
        {
            EnsureCollections();
            return routePoints;
        }
    }

    public IReadOnlyList<WalkRoutePoint> RoutePointSamples
    {
        get
        {
            EnsureCollections();
            return routePointSamples;
        }
    }

    private void Awake()
    {
        EnsureCollections();

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

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        EnsureCollections();
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
        currentSessionId = Guid.NewGuid().ToString("N");
        sessionStartUtc = DateTime.UtcNow.ToString("o");
        sessionEndUtc = "";
        lastSavedWalkFilePath = "";
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
        sessionEndUtc = DateTime.UtcNow.ToString("o");
        walkingSessionActive = false;
        recordRoutePoints = false;

        var savedPath = SaveCurrentWalkingSession();
        sessionStatus = string.IsNullOrEmpty(savedPath)
            ? $"Walk ended: {WalkingSessionSteps} steps, {sessionDistanceMeters:0}m. Save failed."
            : $"Walk saved: {WalkingSessionSteps} steps, {sessionDistanceMeters:0}m.";
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
        EnsureCollections();
        routePoints.Clear();
        routePointSamples.Clear();
        sessionDistanceMeters = 0f;
        lastRoutePointTime = -1f;
    }

    public List<Vector2> GetRoutePointsSnapshot()
    {
        EnsureCollections();
        return new List<Vector2>(routePoints);
    }

    public List<WalkRoutePoint> GetRoutePointSamplesSnapshot()
    {
        EnsureCollections();
        var snapshot = new List<WalkRoutePoint>(routePointSamples.Count);

        foreach (var point in routePointSamples)
        {
            snapshot.Add(point.Clone());
        }

        return snapshot;
    }

    public string SaveCurrentWalkingSession()
    {
        if (!hasWalkingSession)
        {
            Debug.LogWarning("Cannot save walk because no walking session has started.");
            return "";
        }

        var record = CreateSavedWalkSession();

        try
        {
            Directory.CreateDirectory(SavedWalkDirectoryPath);
            var filePath = Path.Combine(SavedWalkDirectoryPath, BuildSavedWalkFileName(record));
            File.WriteAllText(filePath, JsonUtility.ToJson(record, true));
            lastSavedWalkFilePath = filePath;
            Debug.Log($"Walk session saved to {filePath}");
            return filePath;
        }
        catch (Exception exception)
        {
            lastSavedWalkFilePath = "";
            Debug.LogError($"Failed to save walk session: {exception.Message}");
            return "";
        }
    }

    public List<string> GetSavedWalkFilePaths()
    {
        try
        {
            if (!Directory.Exists(SavedWalkDirectoryPath))
            {
                return new List<string>();
            }

            var files = Directory.GetFiles(SavedWalkDirectoryPath, "walk_*.json");
            Array.Sort(files);
            return new List<string>(files);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to list saved walks: {exception.Message}");
            return new List<string>();
        }
    }

    public List<SavedWalkSession> LoadSavedWalks()
    {
        var savedWalks = new List<SavedWalkSession>();
        var filePaths = GetSavedWalkFilePaths();

        foreach (var filePath in filePaths)
        {
            if (TryLoadSavedWalk(filePath, out var savedWalk))
            {
                savedWalks.Add(savedWalk);
            }
        }

        return savedWalks;
    }

    public bool TryLoadSavedWalk(string filePath, out SavedWalkSession savedWalk)
    {
        savedWalk = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            savedWalk = JsonUtility.FromJson<SavedWalkSession>(json);
            if (savedWalk != null)
            {
                savedWalk.EnsureCollections();
            }

            return savedWalk != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to load walk session from {filePath}: {exception.Message}");
            return false;
        }
    }

    private void AddRoutePoint(float latitude, float longitude)
    {
        EnsureCollections();

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
                    ReplaceRoutePointSample(0, latitude, longitude, now);
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
        routePointSamples.Add(CreateRoutePointSample(latitude, longitude, now));
        lastRoutePointTime = now;
    }

    private SavedWalkSession CreateSavedWalkSession()
    {
        EnsureCollections();

        var record = new SavedWalkSession
        {
            id = string.IsNullOrEmpty(currentSessionId) ? Guid.NewGuid().ToString("N") : currentSessionId,
            startedAtUtc = string.IsNullOrEmpty(sessionStartUtc) ? DateTime.UtcNow.ToString("o") : sessionStartUtc,
            endedAtUtc = string.IsNullOrEmpty(sessionEndUtc) ? DateTime.UtcNow.ToString("o") : sessionEndUtc,
            steps = WalkingSessionSteps,
            distanceMeters = sessionDistanceMeters,
            durationSeconds = WalkingSessionDurationSeconds,
            routePointCount = routePointSamples.Count,
            finalLatitude = hasLocation ? latitude : 0f,
            finalLongitude = hasLocation ? longitude : 0f,
            finalAccuracyMeters = hasLocation ? horizontalAccuracy : 0f,
            accuracyStatus = accuracyStatus
        };

        foreach (var point in routePointSamples)
        {
            record.routePoints.Add(point.Clone());
        }

        return record;
    }

    private static string BuildSavedWalkFileName(SavedWalkSession record)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var id = string.IsNullOrEmpty(record.id) ? Guid.NewGuid().ToString("N") : record.id;
        var shortId = id.Length > 8 ? id.Substring(0, 8) : id;
        return $"walk_{timestamp}_{shortId}.json";
    }

    private WalkRoutePoint CreateRoutePointSample(float latitude, float longitude, float now)
    {
        return new WalkRoutePoint
        {
            latitude = latitude,
            longitude = longitude,
            accuracyMeters = horizontalAccuracy,
            secondsSinceSessionStart = hasWalkingSession ? Mathf.Max(0f, now - sessionStartTime) : 0f
        };
    }

    private void ReplaceRoutePointSample(int index, float latitude, float longitude, float now)
    {
        EnsureCollections();

        var sample = CreateRoutePointSample(latitude, longitude, now);

        if (index >= 0 && index < routePointSamples.Count)
        {
            routePointSamples[index] = sample;
            return;
        }

        routePointSamples.Add(sample);
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

    private void EnsureCollections()
    {
        if (routePoints == null)
        {
            routePoints = new List<Vector2>();
        }

        if (routePointSamples == null)
        {
            routePointSamples = new List<WalkRoutePoint>();
        }
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

    public string getLastSavedWalkFilePath()
    {
        return LastSavedWalkFilePath;
    }

    [Serializable]
    public class SavedWalkSession
    {
        public string id;
        public string startedAtUtc;
        public string endedAtUtc;
        public int steps;
        public float distanceMeters;
        public float durationSeconds;
        public int routePointCount;
        public float finalLatitude;
        public float finalLongitude;
        public float finalAccuracyMeters;
        public string accuracyStatus;
        public List<WalkRoutePoint> routePoints = new List<WalkRoutePoint>();

        public void EnsureCollections()
        {
            if (routePoints == null)
            {
                routePoints = new List<WalkRoutePoint>();
            }
        }
    }

    [Serializable]
    public class WalkRoutePoint
    {
        public float latitude;
        public float longitude;
        public float accuracyMeters;
        public float secondsSinceSessionStart;

        public WalkRoutePoint Clone()
        {
            return new WalkRoutePoint
            {
                latitude = latitude,
                longitude = longitude,
                accuracyMeters = accuracyMeters,
                secondsSinceSessionStart = secondsSinceSessionStart
            };
        }
    }
}


