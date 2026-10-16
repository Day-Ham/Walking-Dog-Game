using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;



public class StepCountAndGpsManager : MonoBehaviour, ISerializationCallbackReceiver
{
    //variables
    public const float AccurateGpsThresholdMeters = 10f;

    private const float EarthRadiusMeters = 6371000f;
    private WalkGpsFilter gpsFilter = new WalkGpsFilter();
    private double locationTimestamp;
    private double routeEpoch;
    private float routeElapsedOffset;
    private float nextCheckpointTime;
    private int recoveredSteps;
    private LocalWalkRepository checkpoints;
    private AndroidWalkTracking backgroundTracking;
    private long nativeSequence;
    private string recoveryError = "";
    private int sessionTerritoryVersion;
    private int sessionStepAccountingVersion;
    private ContinuousSteps continuousSteps;
    public ContinuousSteps ActivitySteps => continuousSteps ?? (continuousSteps =
        new ContinuousSteps(Path.Combine(LocalWalks.DirectoryPath, "ContinuousSteps")));
    private string StepOwner => cloudStore?.AuthenticatedUserId ?? "";
    public long TotalTrackedSteps { get { try { return ActivitySteps.Get(StepOwner).total; } catch { return 0; } } }
    public string StepTrackingStatus => ActivitySteps.Error;
    // Initializes continuous step counting sensor source from local storage.
    public void BeginStepSensorSource() => ActivitySteps.BeginSensorSource();
    private TerritoryService territoryService;
    private float nextTerritoryRetry;


    public int territtoryFlag = 0; // for UI Color change with gps text will do this later

    // A preview derived from recorded steps, not a saved/spendable currency balance.
    private const int StepsPerPoint = 10;
    public int StepPoints => Steps / StepsPerPoint;
    public int WalkingSessionPoints => WalkingSessionSteps / StepsPerPoint;

    //territory functions
    
    public TerritoryService Territories
    {
        get
        {
            var owner = cloudStore?.AuthenticatedUserId ?? "";
            if (territoryService == null || territoryService.Owner != owner)
            {
                territoryService = territoryService ?? new TerritoryService(LocalWalks);
                territoryService.Refresh(owner);
                nextTerritoryRetry = Time.realtimeSinceStartup + 30f;
            }
            return territoryService;
        }
    }
    public string TerritorySummary => Territories.Summary(currentSessionId, sessionOwnerUserId, HasUnsavedCompletedWalk);

    private LocalWalkRepository Checkpoints => checkpoints ??
        (checkpoints = new LocalWalkRepository(Path.Combine(LocalWalks.DirectoryPath, "Active")));
    internal static double UtcSeconds => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
    public bool HasFreshLocation => HasAccurateLocation && UtcSeconds - locationTimestamp <= WalkGpsFilter.FreshSeconds;
    public bool HasTrackingGaps => gpsFilter.HasGaps;
    public bool IsBackgroundTracking => backgroundTracking != null && backgroundTracking.IsRunning;
    public string BackgroundTrackingStatus => IsBackgroundTracking ? "Screen-off route recording on"
        : !string.IsNullOrEmpty(backgroundTracking?.Error) ? backgroundTracking.Error : "Keep the app open to record your route";
    public string RecoveryError => recoveryError;
    public bool HasUnsavedCompletedWalk => completedSessionRecord != null && string.IsNullOrEmpty(lastSavedWalkFilePath);
    public string TrackingStatus => walkingSessionActive ? gpsFilter.Status : (HasFreshLocation ? "GPS ready" : "Waiting for GPS");
    public SavedWalkSession RecoverableWalk
    {
        get
        {
            var owner = cloudStore?.AuthenticatedUserId ?? "";
            return Checkpoints.LoadAll().Find(w => w.ownerUserId == owner && LocalWalks.Find(w.id) == null);
        }
    }

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
    [SerializeField] private string sessionOwnerUserId = "";

    private LocalWalkRepository localWalks;
    private IWalkCloudStore cloudStore;
    private WalkSyncService walkSync;
    private CancellationTokenSource syncLifetime;
    private float nextSyncTime;
    private string lastWalkSaveState = "Ready";
    private SavedWalkSession completedSessionRecord;

    public LocalWalkRepository LocalWalks => localWalks ??
        (localWalks = new LocalWalkRepository(SavedWalkDirectoryPath));
    public string LastWalkSaveState => lastWalkSaveState;

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
    public int WalkingSessionSteps => recoveredSteps + Mathf.Max(0, (walkingSessionActive ? stepsCounted : sessionEndSteps) - sessionStartSteps);
   
    
    public int getTFlag() { return territtoryFlag;}

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
   
    public bool HasRouteStart => routePoints != null && routePoints.Count > 0;
    public float DistanceToRouteStartMeters
    {
        get
        {
            // A stale or inaccurate fix must not tell the player they can close a loop.
            if (!HasRouteStart || !HasFreshLocation) return -1f;
            return CalculateDistanceMeters(latitude, longitude, routePoints[0].x, routePoints[0].y);
        }
    }
    public bool IsWithinTerritoryClosureRange => DistanceToRouteStartMeters >= 0f
        && DistanceToRouteStartMeters <= TerritoryCapture.ClosureMeters;
    public string TerritoryClosureStatus
    {
        get
        {
            territtoryFlag = 0;
            if (!walkingSessionActive || sessionTerritoryVersion != TerritoryCapture.Version || !HasRouteStart)
                return "";

            var distance = DistanceToRouteStartMeters;
            if (distance < 0f) return "";
            if (HasTrackingGaps) return "GPS gaps prevent a territory claim. Your walk will still save.";
            if (sessionDistanceMeters < TerritoryCapture.MinimumTrailMeters)
                return $"Walk {TerritoryCapture.MinimumTrailMeters - sessionDistanceMeters:0} m more to reach the trail minimum.";
            if (sessionDistanceMeters < TerritoryCapture.MinimumLoopMeters)
            {
                territtoryFlag = 2;
                return "Trail distance reached. End walk to validate your trail, or keep walking for a loop.";
            }

            if (distance <= TerritoryCapture.ClosureMeters)
            {    // This only confirms the closing-distance requirement; area, route length,
                // and non-crossing checks are still validated when the walk is saved.
              
                territtoryFlag = 2; //signal to green
                return $"Loop closing range reached ({distance:0} m from start). End walk to validate your trail and loop.";
               
            }
            
            territtoryFlag = 1; //signal to yellow

            return $"Trail distance reached. End walk to validate it, or return {distance - (float)TerritoryCapture.ClosureMeters:0} m toward start for a loop.";
        }
    }
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
        syncLifetime = new CancellationTokenSource();
        // Ask once at startup so local walk alerts have permission before a walk begins.
        LocalPhoneNotifications.Initialize();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            backgroundTracking?.Stop();
            if (walkingSessionActive) SaveCheckpoint();
            syncLifetime?.Cancel();
            syncLifetime?.Dispose();
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

    // Updates cumulative step count, checks milestone alerts, and updates active walk progress.
    public void SetStep(int steps)
    {
        ActivitySteps.Observe(StepOwner, steps, walkingSessionActive && sessionStepAccountingVersion == 0);
        stepsCounted = Mathf.Max(0, steps);

        // Trigger the local alert from the phone's saved step total. GPS and a Firebase refresh
        // are not needed; Firebase still validates the free pull when the player claims it.
        NotifyLocalMilestoneFromSteps();

        if (walkingSessionActive)
        {
            sessionEndSteps = stepsCounted;
        }
    }

    // Sends a local notification once when total steps reach an unalerted 10,000-step milestone.
    private void NotifyLocalMilestoneFromSteps()
    {
        var owner = StepOwner;
        if (string.IsNullOrEmpty(owner)) return;
        var totalFreeRolls = PointsWalletSnapshot.MilestoneRollsForSteps(TotalTrackedSteps);
        if (totalFreeRolls <= 0) return;

        var claimed = GetComponent<FirebaseWalkBootstrap>()?.Wallet?.Snapshot?.MilestoneRollsClaimed ?? 0;
        LocalPhoneNotifications.NotifyNewMilestone(owner, totalFreeRolls,
            Math.Max(1, totalFreeRolls - claimed));
    }

    // Returns the current counted steps.
    public int GetSteps()
    {
        return stepsCounted;
    }

    // Resets the in-memory counted step count to zero.
    public void ResetSteps()
    {
        stepsCounted = 0;

        if (!walkingSessionActive)
        {
            sessionStartSteps = 0;
            sessionEndSteps = 0;
        }
    }

    // Records GPS location using current UTC time.
    public void SetGpsLocation(float latitude, float longitude, float accuracyMeters)
    {
        SetGpsLocation(latitude, longitude, accuracyMeters, UtcSeconds, UtcSeconds);
    }

    // Ingests a new GPS fix and observation timestamp from an external tracker or background service.
    public void SetGpsLocation(float latitude, float longitude, float accuracyMeters, double timestamp, double observedAt)
    {
        // Native service owns active route samples, including those recorded with the screen off.
        if (walkingSessionActive && IsBackgroundTracking) return;
        ApplyGpsSample(latitude, longitude, accuracyMeters, timestamp, observedAt);
    }

    // Validates GPS accuracy and appends coordinate to the active route if valid.
    internal void ApplyGpsSample(float latitude, float longitude, float accuracyMeters, double timestamp, double observedAt)
    {
        if (!IsUsableGpsFix(latitude, longitude, accuracyMeters))
        {
            InterruptTracking("Waiting for valid GPS fix");
            return;
        }
        if (!WalkGpsFilter.Finite(timestamp) || timestamp <= 0 || !WalkGpsFilter.Finite(observedAt)
            || observedAt - timestamp > WalkGpsFilter.FreshSeconds || timestamp - observedAt > 2)
        { InterruptTracking("Tracking interrupted — waiting for fresh GPS"); return; }
        if (timestamp <= locationTimestamp) return;

        this.latitude = latitude;
        this.longitude = longitude;
        horizontalAccuracy = Mathf.Max(0f, accuracyMeters);
        hasLocation = true;
        locationTimestamp = timestamp;

        if (HasAccurateLocation)
        {
            accuracyStatus = $"Accurate ({horizontalAccuracy:0.0}m)";

            if (recordRoutePoints)
            {
                AddRoutePoint(latitude, longitude, timestamp, observedAt);
            }

            return;
        }

        accuracyStatus = $"Inaccurate ({horizontalAccuracy:0.0}m)";
        if (recordRoutePoints) gpsFilter.Interrupt("Weak GPS — move to an open area");
    }

    // Invalidates current GPS fix and sets tracking interruption status reason.
    public void InterruptTracking(string reason)
    {
        hasLocation = false;
        accuracyStatus = reason;
        if (recordRoutePoints) gpsFilter.Interrupt(reason);
    }

    // Updates user-facing GPS accuracy status text.
    public void SetGpsStatus(string status)
    {
        accuracyStatus = string.IsNullOrWhiteSpace(status) ? "No GPS status." : status;
    }

    // Starts a new walking session, begins route recording, and starts background tracking.
    public void BeginWalkingSession(bool clearExistingRoute = true)
    {
        if (walkingSessionActive) return;
        if (HasUnsavedCompletedWalk)
        { sessionStatus = "Save the finished walk before starting another."; return; }
        if (RecoverableWalk != null)
        { sessionStatus = "Recover the unfinished walk before starting another."; return; }
        // A walking session can collect steps while waiting; its route starts only on a fresh fix.
        gpsFilter = new WalkGpsFilter();
        recoveredSteps = 0;
        nativeSequence = 0;
        routeEpoch = UtcSeconds;
        routeElapsedOffset = 0;
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
        completedSessionRecord = null;
        sessionOwnerUserId = cloudStore?.AuthenticatedUserId ?? "";
        sessionTerritoryVersion = string.IsNullOrWhiteSpace(sessionOwnerUserId) ? 0 : TerritoryCapture.Version;
        sessionStepAccountingVersion = 1;
        lastWalkSaveState = "Walking";
        sessionStatus = "Walking session active.";

        // Never join the previous walk to a new session, including legacy scene bindings.
        StartRouteRecording(true);

        if (HasFreshLocation)
        {
            AddRoutePoint(latitude, longitude, locationTimestamp, UtcSeconds);
        }
        SaveCheckpoint();
        StartBackgroundTracking();
    }

    // Concludes the active walking session, saves it to disk, and updates status.
    public void EndWalkingSession()
    {
        if (!walkingSessionActive)
        {
            return;
        }

        backgroundTracking?.Stop();
        SaveCheckpoint();
        sessionEndSteps = stepsCounted;
        sessionEndTime = Time.realtimeSinceStartup;
        sessionEndUtc = DateTime.UtcNow.ToString("o");
        walkingSessionActive = false;
        recordRoutePoints = false;

        var savedPath = SaveCurrentWalkingSession();
        sessionStatus = string.IsNullOrEmpty(savedPath)
            ? $"Walk ended: {WalkingSessionSteps} steps, {sessionDistanceMeters:0}m. Save failed."
            : $"Walk saved on device: {WalkingSessionSteps} steps, {sessionDistanceMeters:0}m.";
    }

    // Toggles between starting and ending a walking session.
    public void ToggleWalkingSession()
    {
        if (walkingSessionActive)
        {
            EndWalkingSession();
            return;
        }

        BeginWalkingSession();
    }

    // Enables collection of GPS route breadcrumb points.
    public void StartRouteRecording(bool clearExistingRoute = false)
    {
        if (clearExistingRoute)
        {
            ClearRoute();
        }

        recordRoutePoints = true;
    }

    // Stops recording route points.
    public void StopRouteRecording()
    {
        recordRoutePoints = false;
    }

    // Clears recorded route points and resets session distance.
    public void ClearRoute()
    {
        EnsureCollections();
        routePoints.Clear();
        routePointSamples.Clear();
        sessionDistanceMeters = 0f;
        lastRoutePointTime = -1f;
    }

    // Returns a copy of the recorded route coordinates.
    public List<Vector2> GetRoutePointsSnapshot()
    {
        EnsureCollections();
        return new List<Vector2>(routePoints);
    }

    // Returns a deep copy of route point samples with timestamps and accuracy.
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

    // Saves completed walk to disk, clears checkpoints, and triggers cloud sync.
    public string SaveCurrentWalkingSession()
    {
        if (!hasWalkingSession || walkingSessionActive)
        {
            Debug.LogWarning("Only completed walking sessions can be saved.");
            return "";
        }

        // GPS and global step state may continue changing after Stop Walk.
        // Preserve the exact completed payload for repeated saves/disk retries.
        var record = completedSessionRecord ?? (completedSessionRecord = CreateSavedWalkSession());

        try
        {
            var filePath = LocalWalks.Save(record);
            ClearCheckpoint(record.id);
            lastSavedWalkFilePath = filePath;
            // TerritoryService contains its own errors; territory failures never undo a saved walk.
            Territories.Refresh(cloudStore?.AuthenticatedUserId ?? "");
            RefreshLastWalkSaveState();
            GetComponent<FirebaseWalkBootstrap>()?.WalkSaved();
            RequestWalkSync();
            return filePath;
        }
        catch (Exception exception)
        {
            lastSavedWalkFilePath = "";
            lastWalkSaveState = "Save failed";
            Debug.LogError($"Failed to save walk session: {exception.Message}");
            return "";
        }
    }

    // Returns file paths of all saved walk sessions in local storage.
    public List<string> GetSavedWalkFilePaths()
    {
        return LocalWalks.GetFilePaths();
    }

    // Loads all saved walk sessions from local disk.
    public List<SavedWalkSession> LoadSavedWalks()
    {
        return LocalWalks.LoadAll();
    }

    // Attempts to load a specific walk session file from disk.
    public bool TryLoadSavedWalk(string filePath, out SavedWalkSession savedWalk)
    {
        return LocalWalks.TryLoad(filePath, out savedWalk);
    }

    // FirebaseWalkBootstrap supplies the authenticated adapter here when attached.
    // Configures cloud sync provider and kicks off pending walk synchronization.
    public void ConfigureCloudSync(IWalkCloudStore store)
    {
        if (walkSync != null && walkSync.IsRunning)
            throw new InvalidOperationException("Wait for the current sync before replacing its provider.");
        cloudStore = store;
        walkSync = store == null ? null : new WalkSyncService(LocalWalks, store);
        RequestWalkSync();
    }

    // Assigns an anonymous local walk file to the current authenticated user ID.
    public void AssignLocalWalkToCurrentAccount(string walkId)
    {
        var owner = cloudStore?.AuthenticatedUserId;
        if (string.IsNullOrWhiteSpace(owner)) throw new InvalidOperationException("Sign in before importing a local walk.");
        LocalWalks.AssignOwner(walkId, owner);
        RefreshLastWalkSaveState();
        RequestWalkSync();
    }

    // Asynchronously uploads unsynced local walks to cloud storage.
    public Task SyncPendingWalksAsync(CancellationToken cancellationToken = default)
    {
        return walkSync == null ? Task.CompletedTask : walkSync.SyncPendingAsync(cancellationToken);
    }

    private void Update()
    {
        backgroundTracking?.Pump();
        var territories = Territories;
        if (!string.IsNullOrEmpty(territories.Error) && Time.realtimeSinceStartup >= nextTerritoryRetry)
        {
            nextTerritoryRetry = Time.realtimeSinceStartup + 30f;
            territories.Refresh(cloudStore?.AuthenticatedUserId ?? "");
        }
        if (walkingSessionActive)
        {
            if (hasLocation && UtcSeconds - locationTimestamp > WalkGpsFilter.GapSeconds)
                InterruptTracking("Tracking interrupted — waiting for GPS");
            if (Time.realtimeSinceStartup >= nextCheckpointTime) SaveCheckpoint();
        }
        if (walkSync == null || Time.realtimeSinceStartup < nextSyncTime) return;
        RequestWalkSync();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) ActivitySteps.Flush(StepOwner);
        if (paused && walkingSessionActive)
        {
            if (!IsBackgroundTracking) InterruptTracking("Tracking interrupted — app paused");
            SaveCheckpoint();
        }
        if (!paused) backgroundTracking?.Pump();
        if (!paused) RequestWalkSync();
    }

    private void OnApplicationQuit()
    {
        ActivitySteps.Flush(StepOwner);
        backgroundTracking?.Stop();
        if (walkingSessionActive) SaveCheckpoint();
    }

    // Saves an active walk snapshot to disk for crash recovery.
    public void SaveCheckpoint()
    {
        if (!walkingSessionActive) return;
        nextCheckpointTime = Time.realtimeSinceStartup + 15f;
        try { Checkpoints.SaveCheckpoint(CreateSavedWalkSession()); recoveryError = ""; }
        catch (Exception e)
        {
            recoveryError = "Recovery save failed. Keep the app open and finish your walk.";
            Debug.LogWarning("Walk checkpoint failed: " + e.GetType().Name);
        }
    }

    // Cleans up a temporary checkpoint file once walk is successfully finalized.
    private void ClearCheckpoint(string id)
    {
        try { Checkpoints.RemoveCheckpoint(id); }
        catch (Exception e) { Debug.LogWarning("Completed walk saved; checkpoint cleanup deferred: " + e.GetType().Name); }
    }

    // Restores an interrupted walk session from an active disk checkpoint.
    public bool RecoverWalk(bool continueWalking)
    {
        if (walkingSessionActive) return false;
        var saved = RecoverableWalk;
        if (saved == null) return false;
        hasWalkingSession = true;
        walkingSessionActive = true;
        recordRoutePoints = true;
        currentSessionId = saved.id;
        sessionOwnerUserId = saved.ownerUserId;
        sessionTerritoryVersion = saved.territoryVersion;
        sessionStepAccountingVersion = saved.stepAccountingVersion;
        sessionStartUtc = saved.startedAtUtc;
        sessionEndUtc = "";
        sessionStartTime = Time.realtimeSinceStartup - saved.durationSeconds;
        sessionEndTime = Time.realtimeSinceStartup;
        recoveredSteps = saved.steps;
        sessionStartSteps = sessionEndSteps = stepsCounted;
        sessionDistanceMeters = saved.distanceMeters;
        nativeSequence = saved.nativeSequence;
        routeEpoch = LocalWalkRepository.ParseUtc(saved.endedAtUtc).ToUnixTimeMilliseconds() / 1000d;
        routeElapsedOffset = saved.durationSeconds;
        completedSessionRecord = null;
        lastSavedWalkFilePath = "";
        routePointSamples = saved.routePoints;
        routePoints = new List<Vector2>();
        foreach (var point in routePointSamples) routePoints.Add(new Vector2(point.latitude, point.longitude));
        gpsFilter = new WalkGpsFilter();
        gpsFilter.Restore(routePoints.Count > 0, saved.hasTrackingGaps);
        hasLocation = false;
        locationTimestamp = 0;
        lastWalkSaveState = "Walk recovered";
        // Recover any durable native samples before starting a new segment.
        backgroundTracking = new AndroidWalkTracking(this);
        backgroundTracking.Recover(saved);
        gpsFilter.Interrupt("Waiting for GPS after recovery");
        // Offline time is excluded when the native service was no longer recording.
        routeEpoch = UtcSeconds;
        routeElapsedOffset = WalkingSessionDurationSeconds;
        if (continueWalking) { SaveCheckpoint(); StartBackgroundTracking(); }
        else EndWalkingSession();
        return true;
    }

    // Starts native Android foreground tracking service for screen-off location logging.
    private void StartBackgroundTracking()
    {
        backgroundTracking = backgroundTracking ?? new AndroidWalkTracking(this);
        // Give Android the first accepted route point and distance already walked. This lets
        // the local territory notification work even while Unity is in the background.
        var canClaimTerritory = sessionTerritoryVersion == TerritoryCapture.Version && HasRouteStart;
        var start = canClaimTerritory ? routePoints[0] : Vector2.zero;
        backgroundTracking.Start(currentSessionId, LocalWalks.DirectoryPath, canClaimTerritory,
            start.x, start.y, sessionDistanceMeters, (float)TerritoryCapture.ClosureMeters);
    }

    internal long NativeSequence => nativeSequence;
    // Adjusts session start time to account for background elapsed duration.
    internal void ExtendRecoveredDuration(float seconds) { sessionStartTime -= Mathf.Max(0, seconds); }
    // Applies GPS sample received from the Android background tracking service.
    internal void ApplyNativeSample(AndroidWalkTracking.Sample sample)
    {
        if (!walkingSessionActive || sample.sequence <= nativeSequence) return;
        if (sample.interrupted) InterruptTracking("Tracking interrupted — GPS unavailable");
        else ApplyGpsSample(sample.latitude, sample.longitude, sample.accuracy, sample.timestamp, sample.observedAt);
        nativeSequence = sample.sequence;
    }

    // Initiates background cloud walk sync if cooldown timer has elapsed.
    private async void RequestWalkSync()
    {
        nextSyncTime = Time.realtimeSinceStartup + 15f;
        if (walkSync == null || syncLifetime == null || syncLifetime.IsCancellationRequested) return;
        try
        {
            await SyncPendingWalksAsync(syncLifetime.Token);
            if (this != null) RefreshLastWalkSaveState();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogWarning("Walk sync could not finish; local saves are retained. " + exception.GetType().Name);
        }
    }

    // Updates UI state string indicating whether walk is saved locally or synced to cloud.
    private void RefreshLastWalkSaveState()
    {
        if (walkingSessionActive || string.IsNullOrEmpty(lastSavedWalkFilePath)) return;
        if (!LocalWalks.TryLoad(lastSavedWalkFilePath, out var walk))
        {
            lastWalkSaveState = "Save unavailable";
            return;
        }
        lastWalkSaveState = walk.sync.state == WalkUploadState.Synced
            ? "Summary synced to cloud"
            : walk.sync.state == WalkUploadState.RetryNeeded
                ? "Saved on device - sync pending"
                : "Saved on device";
    }

    // Validates and adds GPS coordinate to active route, accumulating total distance.
    private void AddRoutePoint(float latitude, float longitude, double timestamp, double observedAt)
    {
        EnsureCollections();
        if (!gpsFilter.Accept(latitude, longitude, horizontalAccuracy, timestamp, observedAt, out var startsSegment)) return;
        var point = new Vector2(latitude, longitude);
        if (routePoints.Count > 0 && !startsSegment)
            sessionDistanceMeters += CalculateDistanceMeters(routePoints[routePoints.Count - 1], point);
        var elapsed = Mathf.Clamp(routeElapsedOffset + (float)(timestamp - routeEpoch), 0, WalkingSessionDurationSeconds);
        if (routePointSamples.Count > 0) elapsed = Mathf.Max(elapsed, routePointSamples[routePointSamples.Count - 1].secondsSinceSessionStart);
        routePoints.Add(point);
        routePointSamples.Add(new WalkRoutePoint
        {
            latitude = latitude, longitude = longitude, accuracyMeters = horizontalAccuracy,
            secondsSinceSessionStart = elapsed, gpsTimestamp = timestamp, startsNewSegment = startsSegment
        });
        lastRoutePointTime = Time.realtimeSinceStartup;
    }

    // Constructs a serializable walk session record from current session data.
    private SavedWalkSession CreateSavedWalkSession()
    {
        EnsureCollections();

        var record = new SavedWalkSession
        {
            schemaVersion = LocalWalkRepository.CurrentSchemaVersion,
            ownerUserId = sessionOwnerUserId,
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
            accuracyStatus = accuracyStatus,
            trackingVersion = 1,
            territoryVersion = sessionTerritoryVersion,
            stepAccountingVersion = sessionStepAccountingVersion,
            hasTrackingGaps = gpsFilter.HasGaps,
            nativeSequence = nativeSequence
        };

        foreach (var point in routePointSamples)
        {
            record.routePoints.Add(point.Clone());
        }

        return record;
    }

    // Validates that coordinates and accuracy are within valid physical limits.
    private static bool IsUsableGpsFix(float latitude, float longitude, float accuracyMeters)
    {
        if (!WalkGpsFilter.Finite(latitude) || !WalkGpsFilter.Finite(longitude)
            || !WalkGpsFilter.Finite(accuracyMeters) || accuracyMeters <= 0f)
        {
            return false;
        }

        if (latitude < -90f || latitude > 90f || longitude < -180f || longitude > 180f)
        {
            return false;
        }

        return true;
    }

    // Ensures route point collection lists are initialized.
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

    // Overload calculating distance between two Vector2 coordinates in meters.
    public static float CalculateDistanceMeters(Vector2 from, Vector2 to)
    {
        return CalculateDistanceMeters(from.x, from.y, to.x, to.y);
    }

    // Calculates great-circle distance between two GPS coordinates using Haversine formula.
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



    //
    //      GETTERS AND SETTERS 
    //

    public int getPoint() { return StepPoints; }

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
        public int schemaVersion;
        public string ownerUserId = "";
        public WalkSyncMetadata sync = new WalkSyncMetadata();
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
        // Legacy records remain readable, but have no verified continuity information.
        public int trackingVersion;
        public int territoryVersion;
        // Zero: legacy per-walk rewards. One: steps are credited independently.
        public int stepAccountingVersion;
        public bool hasTrackingGaps;
        public long nativeSequence;
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
        public double gpsTimestamp;
        public bool startsNewSegment;

        public WalkRoutePoint Clone()
        {
            return new WalkRoutePoint
            {
                latitude = latitude,
                longitude = longitude,
                accuracyMeters = accuracyMeters,
                secondsSinceSessionStart = secondsSinceSessionStart,
                gpsTimestamp = gpsTimestamp,
                startsNewSegment = startsNewSegment
            };
        }
    }
}


