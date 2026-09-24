using System;
using System.Threading.Tasks;
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
        if (Wallet.IsLoading || Time.realtimeSinceStartup < nextWalletRefresh) return;
        Wallet.HasPendingWalks = manager.LocalWalks.LoadAll().Exists(w => w.ownerUserId == Wallet.Owner
            && w.steps >= 10 && w.sync.state != WalkUploadState.Synced);
        nextWalletRefresh = Time.realtimeSinceStartup + (Wallet.Snapshot?.IsReconciling == true ? 2 : 30);
        _ = Wallet.RefreshAsync();
    }

    private void OnApplicationPause(bool paused) { if (!paused) nextWalletRefresh = 0; }

    private void OnWalletAccountChanged(object sender, EventArgs args)
    {
        Wallet?.Reset();
        nextWalletRefresh = 0;
    }

    internal void WalkSaved()
    {
        if (Wallet != null) { Wallet.SynchronizeAccount(); Wallet.HasPendingWalks = true; }
        nextWalletRefresh = 0;
    }

    private void OnDestroy()
    {
        destroyed = true;
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
