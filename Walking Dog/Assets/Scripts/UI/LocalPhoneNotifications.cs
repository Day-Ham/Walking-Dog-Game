using UnityEngine;

 // local android notification script
public static class LocalPhoneNotifications
{
    private const string MilestonePreferencePrefix = "localNotification.lastMilestone.";
    private static bool permissionWasRequested;

    /// <summary>
    /// Shows one Android notification when an account reaches a new 10,000-step milestone.
    /// It saves the largest milestone already alerted, so claiming a roll never causes an old
    /// milestone to alert again.
    /// </summary>
    public static void NotifyNewMilestone(string accountId, long totalFreeRolls, long availableRolls)
    {
        if (string.IsNullOrEmpty(accountId) || totalFreeRolls <= 0 || availableRolls <= 0) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        const string notificationPermission = "android.permission.POST_NOTIFICATIONS";
        var requiresRuntimePermission = false;
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            requiresRuntimePermission = version.GetStatic<int>("SDK_INT") >= 33;
        }
        if (requiresRuntimePermission && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(notificationPermission))
        {
            // Android 13+ requires the player's permission before a local notification can appear.
            // The next wallet refresh will notify after the player accepts this prompt.
            if (!permissionWasRequested)
            {
                permissionWasRequested = true;
                UnityEngine.Android.Permission.RequestUserPermission(notificationPermission);
            }
            return;
        }

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
}
