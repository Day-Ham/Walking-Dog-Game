using UnityEngine;

 // local android notification script
public static class LocalPhoneNotifications
{
    // v2 intentionally retries an existing free roll once after the notification-delivery fix.
    private const string MilestonePreferencePrefix = "localNotification.lastMilestone.v2.";
    private static bool permissionWasRequested;

    // Prepares Android notifications and asks for permission early to prevent missed territory alerts.
    public static void Initialize()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        RequestPermissionIfNeeded();
        try
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var service = new AndroidJavaClass("com.walkingdog.tracking.WalkLocationService"))
            {
                service.CallStatic("prepareNotifications", activity);
            }
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("Could not prepare Android notifications: " + error.GetType().Name);
        }
#endif
    }

    // Displays a native Android notification when the account reaches a new 10,000-step milestone.
    public static void NotifyNewMilestone(string accountId, long totalFreeRolls, long availableRolls)
    {
        if (string.IsNullOrEmpty(accountId) || totalFreeRolls <= 0 || availableRolls <= 0) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!RequestPermissionIfNeeded()) return;

        var preferenceKey = MilestonePreferencePrefix + accountId;
        var lastAlerted = PlayerPrefs.GetString(preferenceKey, "0");
        if (!long.TryParse(lastAlerted, out var milestoneAlreadyAlerted)) milestoneAlreadyAlerted = 0;
        if (totalFreeRolls <= milestoneAlreadyAlerted) return;

        // WalkLocationService owns the Android notification channels, keeping all Android-only
        // notification code out of the Unity scene and avoiding an additional package.
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var service = new AndroidJavaClass("com.walkingdog.tracking.WalkLocationService"))
        {
            if (!service.CallStatic<bool>("notifyFreeRoll", activity, availableRolls)) return;
        }

        PlayerPrefs.SetString(preferenceKey, totalFreeRolls.ToString());
        PlayerPrefs.Save();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // Prompts for Android 13+ POST_NOTIFICATIONS runtime permission if not yet granted.
    private static bool RequestPermissionIfNeeded()
    {
        const string notificationPermission = "android.permission.POST_NOTIFICATIONS";
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            if (version.GetStatic<int>("SDK_INT") < 33) return true;
        }
        if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(notificationPermission)) return true;
        if (!permissionWasRequested)
        {
            permissionWasRequested = true;
            UnityEngine.Android.Permission.RequestUserPermission(notificationPermission);
        }
        return false;
    }
#endif
}
