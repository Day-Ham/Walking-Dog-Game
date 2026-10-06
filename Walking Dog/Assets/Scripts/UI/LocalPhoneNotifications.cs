using UnityEngine;

 // local android notification script
public static class LocalPhoneNotifications
{
    // v2 intentionally retries an existing free roll once after the notification-delivery fix.
    private const string MilestonePreferencePrefix = "localNotification.lastMilestone.v2.";
    private static bool permissionWasRequested;

    /// <summary>
    /// Prepares Android notifications and asks for permission early. Calling this when the game
    /// starts prevents a territory alert from being missed because Android had not yet shown its
    /// notification-permission prompt.
    /// </summary>
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

    /// <summary>
    /// Shows one Android notification when an account reaches a new 10,000-step milestone.
    /// It saves the largest milestone already alerted, so claiming a roll never causes an old
    /// milestone to alert again.
    /// </summary>
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
    /// <summary>Returns false until Android 13+ notification permission is granted.</summary>
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
