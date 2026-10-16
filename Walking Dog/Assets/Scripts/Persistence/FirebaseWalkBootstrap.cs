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

    // Begins asynchronous Firebase initialization when the component starts.
    private async void Start()
    {
        await InitializeAsync();
    }

    // Ensures Firebase dependencies and stores are initialized once.
    public Task InitializeAsync()
    {
        if (destroyed || IsReady) return Task.CompletedTask;
        if (initialization != null && !initialization.IsCompleted) return initialization;
        initialization = InitializeCoreAsync();
        return initialization;
    }

    // Resolves Google Play services / Firebase dependencies, creates cloud stores, and binds auth events.
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

    // Schedules periodic background sync for step activity, shared routes, and wallet point balances.
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

    // Checks whether unacknowledged physical steps are waiting for cloud synchronization.
    private bool HasPendingActivity()
    {
        if (string.IsNullOrEmpty(Wallet?.Owner)) return false;
        try { var record = manager.ActivitySteps.Get(Wallet.Owner); return record.total > record.acknowledged; }
        catch { return true; }
    }

    // Uploads unacknowledged step increments to Firestore continuous steps document.
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

    // Awaits background activity upload task without throwing into caller context.
    private static async void ObserveActivityUpload(Task upload)
    { try { await upload; } catch (Exception) { } }

    // Schedules an immediate synchronization pass for publicly shared walk routes.
    public void RequestRouteSharingSync() { nextRouteSync = 0; }

    // Synchronizes locally stored routes with social public routes on Firestore.
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

    // Resets sync timers on app unpause to trigger rapid synchronization.
    private void OnApplicationPause(bool paused) { if (!paused) { nextWalletRefresh = 0; nextActivitySync = 0; nextRouteSync = 0; } }

    // Resets token sources and wallet caches when user signs in, signs out, or switches accounts.
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

    // Notifies the bootstrap that a new walk was saved to trigger immediate wallet and route updates.
    internal void WalkSaved()
    {
        if (Wallet != null) { Wallet.SynchronizeAccount(); Wallet.HasPendingWalks = true; }
        nextWalletRefresh = 0;
        nextRouteSync = 0;
    }

    // Disposes cloud stores, unregisters auth listeners, and releases synchronization resources.
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
