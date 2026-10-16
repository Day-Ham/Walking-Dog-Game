using UnityEngine;

// Establishes consistent frame pacing before the first scene is displayed.
// Android does not use QualitySettings.vSyncCount; without this explicit cap,
// devices can render at an unstable rate and animate unevenly as they heat up.
public static class MobileFramePacing
{
    // Sets target frame rate to 60 FPS on Android prior to loading the first scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure()
    {
#if UNITY_ANDROID 
        // 60 fps is a stable target for the project's 60 Hz UI animation clips.
        // Do not use -1 (uncapped) on phones: it increases heat and frame-time spikes.
        Application.targetFrameRate = 60;
#endif
    }
}
