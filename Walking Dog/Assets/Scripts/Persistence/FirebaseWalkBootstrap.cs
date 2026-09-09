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

            var manager = GetComponent<StepCountAndGpsManager>();
            if (manager != StepCountAndGpsManager.Instance)
                throw new InvalidOperationException("Firebase must use the active walk manager.");
            app = FirebaseApp.DefaultInstance;
            store = new FirebaseWalkCloudStore(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
            manager.ConfigureCloudSync(store);
            IsReady = true;
            Status = "Firebase initialized"; // Does not imply signed in or server connectivity.
            Debug.Log(string.IsNullOrEmpty(store.AuthenticatedUserId)
                ? "Walk cloud sync initialized; sign in to upload walks."
                : "Walk cloud sync initialized for the signed-in player.");
        }
        catch (Exception exception)
        {
            store?.Dispose();
            store = null;
            if (destroyed) return;
            Status = "Firebase initialization failed";
            Debug.LogWarning(Status + "; walks will continue saving on device. " + exception.GetType().Name);
        }
    }

    private void OnDestroy()
    {
        destroyed = true;
        IsReady = false;
        store?.Dispose();
        // Default Firebase instances are shared; do not dispose them here.
        app = null;
    }
}
