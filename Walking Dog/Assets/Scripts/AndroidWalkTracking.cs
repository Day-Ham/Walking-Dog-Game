using System;
using System.IO;
using UnityEngine;

// Unity drains the native journal on the main thread. The sequence is checkpointed
// with the route, so a restart replays only samples not yet durably processed.
internal sealed class AndroidWalkTracking
{
    private readonly StepCountAndGpsManager manager;
    private string walkId, directory;
    private float nextPoll;
    private bool requested;
    public bool IsRunning { get; private set; }
    public string Error { get; private set; } = "";
    private const string ServiceClass = "com.walkingdog.tracking.WalkLocationService";
#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool notificationPermissionRequested;
#endif
    internal AndroidWalkTracking(StepCountAndGpsManager manager) { this.manager = manager; }

    public void Start(string id, string walkDirectory)
    {
        walkId = id;
        directory = Path.Combine(walkDirectory, "Background");
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
        { Error = "Precise location permission is needed for screen-off tracking."; return; }
        try
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var service = new AndroidJavaClass(ServiceClass))
            {
                Error = service.CallStatic<string>("begin", activity, directory, walkId) ?? "";
                requested = IsRunning = string.IsNullOrEmpty(Error);
            }
            // Android 13+ lets the player choose whether the ongoing notification
            // appears in the drawer. Denial does not prevent the location service.
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                const string notifications = "android.permission.POST_NOTIFICATIONS";
                if (requested && version.GetStatic<int>("SDK_INT") >= 33 && !notificationPermissionRequested
                    && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(notifications))
                {
                    notificationPermissionRequested = true;
                    UnityEngine.Android.Permission.RequestUserPermission(notifications);
                }
            }
        }
        catch (Exception e) { Fail(e); }
#endif
    }

    public void Pump()
    {
        if (!requested || Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + 1f;
        // Drain the backlog before the manager evaluates current GPS freshness.
        Read(true);
    }

    public void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(walkId)) return;
        try
        {
            using (var service = new AndroidJavaClass(ServiceClass)) service.CallStatic("finish", walkId);
            Read(true);
        }
        catch (Exception e) { Fail(e); }
#endif
        requested = IsRunning = false;
    }

    public void Recover(StepCountAndGpsManager.SavedWalkSession saved)
    {
        walkId = saved.id;
        directory = Path.Combine(manager.LocalWalks.DirectoryPath, "Background");
        Read(true, LocalWalkRepository.ParseUtc(saved.endedAtUtc).ToUnixTimeMilliseconds() / 1000d);
    }

    private void Read(bool all, double recoveryTime = 0)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var service = new AndroidJavaClass(ServiceClass))
            {
                Batch batch;
                do
                {
                    batch = JsonUtility.FromJson<Batch>(service.CallStatic<string>("read", directory, walkId, manager.NativeSequence));
                    if (batch == null) throw new IOException("Missing tracking batch.");
                    IsRunning = batch.running;
                    Error = batch.error ?? "";
                    if (!string.IsNullOrEmpty(Error)) manager.InterruptTracking("Tracking interrupted — " + Error);
                    if (batch.samples == null) break;
                    foreach (var sample in batch.samples)
                    {
                        if (sample.sequence <= manager.NativeSequence) continue;
                        if (recoveryTime > 0 && sample.observedAt > recoveryTime)
                        {
                            manager.ExtendRecoveredDuration((float)(sample.observedAt - recoveryTime));
                            recoveryTime = sample.observedAt;
                        }
                        manager.ApplyNativeSample(sample);
                    }
                } while (all && batch.samples.Length == 512);
            }
        }
        catch (Exception e) { Fail(e); }
#endif
    }

    private void Fail(Exception e)
    {
        requested = IsRunning = false;
        Error = "Screen-off tracking unavailable. Keep the app open.";
        manager.InterruptTracking(Error);
        Debug.LogWarning("Native walk tracking failed: " + e.GetType().Name);
    }

    [Serializable] internal sealed class Sample
    {
        public long sequence;
        public float latitude, longitude, accuracy;
        public double timestamp, observedAt;
        public bool interrupted;
    }
    [Serializable] private sealed class Batch
    {
        public bool running;
        public string error;
        public Sample[] samples;
    }
}
