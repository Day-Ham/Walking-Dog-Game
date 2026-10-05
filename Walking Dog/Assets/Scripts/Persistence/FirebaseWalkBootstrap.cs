using System;
using System.Threading.Tasks;
using System.Threading;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

// Add to the persistent manager object only after console setup/rules are ready.
// This component does not create accounts, sign in, or claim existing local walks.
[DisallowMultipleComponent]
[RequireComponent(typeof(StepCountAndGpsManager))]
public sealed class FirebaseWalkBootstrap : MonoBehaviour
{
    public bool IsReady { get; private set; }
    public PointsWalletSession Wallet { get; private set; }
    private float nextWalletRefresh;
    private StepCountAndGpsManager manager;
    private FirebaseAuth auth;
    internal IWalkHistoryStore HistoryStore { get; private set; }
    public string Status { get; private set; } = "Not initialized";
    private FirebaseApp app;
    private FirebaseWalkCloudStore store;
    private Task initialization;
    private bool destroyed;
    private CancellationTokenSource activityLifetime = new CancellationTokenSource();
    private bool activitySyncing;
    private float nextActivitySync;
    private WalkingDog.Leaderboards.SocialRouteSync socialRouteSync;
    private float nextRouteSync;
    private bool routeSyncing;

    private async void Start()
    {
        await InitializeAsync();
    }

    // A future login/retry UI can call this on Unity's main thread.
    public Task InitializeAsync()
    {
        if (destroyed || IsReady) return Task.CompletedTask;
        if (initialization != null && !initialization.IsCompleted) return initialization;
        initialization = InitializeCoreAsync();
        return initialization;
    }

    private async Task InitializeCoreAsync()
    {
        Status = "Checking Firebase dependencies";
        try
        {
            var dependencies = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (destroyed) return;
            if (dependencies != DependencyStatus.Available)
            {
                Status = "Firebase unavailable: " + dependencies;
                Debug.LogWarning(Status + ". Walks will continue saving on device.");
                return;
            }

            manager = GetComponent<StepCountAndGpsManager>();
            if (manager != StepCountAndGpsManager.Instance)
                throw new InvalidOperationException("Firebase must use the active walk manager.");
            app = FirebaseApp.DefaultInstance;
            store = new FirebaseWalkCloudStore(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
            Wallet = new PointsWalletSession(new FirebasePointsWalletStore(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance));
            auth = FirebaseAuth.DefaultInstance;
            auth.StateChanged += OnWalletAccountChanged;
            store.PointsChanged += () => nextWalletRefresh = 0;
            manager.ConfigureCloudSync(store);
            socialRouteSync = WalkingDog.Leaderboards.SocialRouteSync.Create(FirebaseFirestore.DefaultInstance,
                () => store?.AuthenticatedUserId ?? "", manager.LocalWalks);
            HistoryStore = new FirebaseWalkHistoryStore(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
            IsReady = true;
            Status = "Firebase initialized"; // Does not imply signed in or server connectivity.
            Debug.Log(string.IsNullOrEmpty(store.AuthenticatedUserId)
                ? "Walk cloud sync initialized; sign in to upload walks."
                : "Walk cloud sync initialized for the signed-in player.");
        }
        catch (Exception exception)
        {
            if (auth != null) auth.StateChanged -= OnWalletAccountChanged;
            auth = null;
            store?.Dispose();
            store = null;
            Wallet?.Dispose();
            Wallet = null;
            HistoryStore = null;
            if (destroyed) return;
            Status = "Firebase initialization failed";
            Debug.LogWarning(Status + "; walks will continue saving on device. " + exception.GetType().Name);
        }
    }

    private void Update()
    {
        if (!IsReady || Wallet == null) return;
        if (Wallet.SynchronizeAccount()) nextWalletRefresh = 0;
        if (!activitySyncing && Time.realtimeSinceStartup >= nextActivitySync) _ = SyncActivityAsync();
        if (!routeSyncing && Time.realtimeSinceStartup >= nextRouteSync) _ = SyncSharedRoutesAsync();
        if (Wallet.IsLoading || Time.realtimeSinceStartup < nextWalletRefresh) return;
        Wallet.HasPendingWalks = manager.LocalWalks.LoadAll().Exists(w => w.ownerUserId == Wallet.Owner
            && w.stepAccountingVersion == 0 && w.steps >= 10 && w.sync.state != WalkUploadState.Synced) || HasPendingActivity();
        nextWalletRefresh = Time.realtimeSinceStartup + (Wallet.Snapshot?.IsReconciling == true ? 2 : 30);
        _ = Wallet.RefreshAsync();
    }

    private bool HasPendingActivity()
    {
        if (string.IsNullOrEmpty(Wallet?.Owner)) return false;
        try { var record = manager.ActivitySteps.Get(Wallet.Owner); return record.total > record.acknowledged; }
        catch { return true; }
    }

    private async Task SyncActivityAsync()
    {
        nextActivitySync = Time.realtimeSinceStartup + 15f;
        var owner = store.AuthenticatedUserId;
        if (string.IsNullOrEmpty(owner)) return;
        activitySyncing = true;
        var token = activityLifetime.Token;
        try
        {
            var saved = manager.ActivitySteps.Snapshot(owner);
            if (saved.total <= saved.acknowledged) return;
            var upload = FirebaseContinuousStepsStore.UploadAsync(FirebaseFirestore.DefaultInstance, saved, token);
            // Firebase transactions cannot be cancelled while offline. A timeout
            // releases this UI; absolute cursors make a later completion safe.
            if (await Task.WhenAny(upload, Task.Delay(TimeSpan.FromSeconds(30), token)) != upload)
            { ObserveActivityUpload(upload); return; }
            await upload;
            if (destroyed || token.IsCancellationRequested || store.AuthenticatedUserId != owner) return;
            manager.ActivitySteps.Acknowledge(saved);
            nextWalletRefresh = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Debug.LogWarning("Step sync pending: " + error.GetType().Name); }
        finally { activitySyncing = false; }
    }

    private static async void ObserveActivityUpload(Task upload)
    { try { await upload; } catch (Exception) { } }

    // The profile toggle requests an immediate pass; periodic passes also repair
    // older ON accounts, newly synced walks and uploads interrupted by app exit.
    public void RequestRouteSharingSync() { nextRouteSync = 0; }

    private async Task SyncSharedRoutesAsync()
    {
        nextRouteSync = Time.realtimeSinceStartup + 15f;
        if (socialRouteSync == null || string.IsNullOrEmpty(store?.AuthenticatedUserId)) return;
        routeSyncing = true;
        try { await socialRouteSync.SyncAsync(activityLifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Debug.LogWarning("Automatic route sharing pending: " + error.GetType().Name); }
        finally { routeSyncing = false; }
    }

    private void OnApplicationPause(bool paused) { if (!paused) { nextWalletRefresh = 0; nextActivitySync = 0; nextRouteSync = 0; } }

    private void OnWalletAccountChanged(object sender, EventArgs args)
    {
        activityLifetime.Cancel();
        activityLifetime.Dispose();
        activityLifetime = new CancellationTokenSource();
        nextActivitySync = 0;
        nextRouteSync = 0;
        Wallet?.Reset();
        nextWalletRefresh = 0;
    }

    internal void WalkSaved()
    {
        if (Wallet != null) { Wallet.SynchronizeAccount(); Wallet.HasPendingWalks = true; }
        nextWalletRefresh = 0;
        nextRouteSync = 0;
    }

    private void OnDestroy()
    {
        destroyed = true;
        activityLifetime.Cancel();
        activityLifetime.Dispose();
        if (auth != null) auth.StateChanged -= OnWalletAccountChanged;
        auth = null;
        IsReady = false;
        HistoryStore = null;
        Wallet?.Dispose();
        Wallet = null;
        store?.Dispose();
        // Default Firebase instances are shared; do not dispose them here.
        app = null;
    }
}
