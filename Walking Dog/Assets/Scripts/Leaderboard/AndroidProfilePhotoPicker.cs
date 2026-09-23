using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WalkingDog.Leaderboards
{
    internal static class AndroidProfilePhotoPicker
    {
        internal static async Task<string> PickAsync(CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("com.walkingdog.profile.ProfilePhotoPicker"))
            {
                bridge.CallStatic("begin", activity);
                try
                {
                    var deadline = DateTime.UtcNow.AddMinutes(3);
                    while (DateTime.UtcNow < deadline)
                    {
                        token.ThrowIfCancellationRequested();
                        var json = bridge.CallStatic<string>("takeResult");
                        if (!string.IsNullOrEmpty(json))
                        {
                            var result = JsonUtility.FromJson<Result>(json);
                            if (!string.IsNullOrEmpty(result.error)) throw new InvalidOperationException(result.error);
                            return string.IsNullOrEmpty(result.photo) ? null : result.photo;
                        }
                        await Task.Delay(100, token);
                    }
                    throw new TimeoutException("Photo picker timed out. Please try again.");
                }
                finally { bridge.CallStatic("cancel"); }
            }
#else
            await Task.CompletedTask;
            throw new InvalidOperationException("Choose a phone photo in the Android app.");
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        [Serializable] private sealed class Result { public string photo; public string error; }
#endif
    }
}
