using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static class AndroidGoogleSignIn
{
    private const string BridgeClass = "com.walkingdog.auth.GoogleSignInBridge";

    public static async Task<string> GetIdTokenAsync(string webClientId, CancellationToken cancellation)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var bridge = new AndroidJavaClass(BridgeClass))
        {
            if (string.IsNullOrWhiteSpace(webClientId)) webClientId = bridge.CallStatic<string>("webClientId", activity);
            if (string.IsNullOrWhiteSpace(webClientId))
                throw new InvalidOperationException("Google sign-in is not configured in this build. Use email/password for now.");
            bridge.CallStatic("begin", activity, webClientId.Trim());
            try
            {
                var deadline = DateTime.UtcNow.AddMinutes(2);
                while (DateTime.UtcNow < deadline)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var json = bridge.CallStatic<string>("takeResult");
                    if (!string.IsNullOrEmpty(json))
                    {
                        var result = JsonUtility.FromJson<Result>(json);
                        if (!string.IsNullOrEmpty(result.error)) throw new InvalidOperationException(result.error);
                        if (string.IsNullOrWhiteSpace(result.idToken)) throw new InvalidOperationException("Google returned no sign-in token. Please retry.");
                        return result.idToken;
                    }
                    await Task.Delay(100, cancellation);
                }
                throw new InvalidOperationException("Google sign-in timed out. Please try again.");
            }
            finally { bridge.CallStatic("cancel"); }
        }
#else
        await Task.CompletedTask;
        throw new InvalidOperationException("Google sign-in is available in the Android app. Use email/password in the editor.");
#endif
    }

    public static void SignOut()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass(BridgeClass)) bridge.CallStatic("signOut", activity);
        }
        catch (Exception) { Debug.LogWarning("Google credential session could not be cleared."); }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    [Serializable] private sealed class Result
    {
        public string idToken;
        public string error;
    }
#endif
}
