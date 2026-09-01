using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using UnityEngine.Android;
#endif

public class StepCounterDisplay : MonoBehaviour
{
    private const string ActivityRecognitionPermission = "android.permission.ACTIVITY_RECOGNITION";
    private const int AndroidQApiLevel = 29;
    private const float MinSecondsBetweenSteps = 0.42f;
    private const float MaxSecondsBetweenSteps = 2.20f;
    private const int RequiredRhythmEvents = 2;

    private StepCounter stepCounter;
    private bool hasPermission = true;
    private bool needsActivityRecognitionPermission;
#if UNITY_ANDROID && !UNITY_EDITOR
    private bool requestedPermission;
    private AndroidJavaObject sensorManager;
    private AndroidJavaObject stepDetectorSensor;
    private StepDetectorListener stepDetectorListener;
    private int detectedStepEvents;
#endif
    private bool checkedStepDetector;
    private bool stepDetectorAvailable;
    private bool stepDetectorRunning;
    private bool hasFirstReading;
    private int startingStepCount;
    private int rawStepCount;
    private int lastRawStepCount;
    private int sessionSteps;
    private int acceptedDetectorSteps;
    private int processedDetectorEvents;
    private int rejectedDetectorEvents;
    private int lastShownDetectorSteps;
    private int rhythmEventCount;
    private float lastRawChangeTime;
    private float lastCandidateStepTime = -1f;
    private float lastDetectorStepTime = -1f;
    private string detectorFilterStatus = "Waiting for walking rhythm...";
    private string status = "Starting step counter...";

    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        StopStepDetector();
    }

    private void Start()
    {
        needsActivityRecognitionPermission = NeedsActivityRecognitionPermission();
        RequestPermissionIfNeeded();
        StartStepDetectorIfPossible();
        TryEnableStepCounter();
    }

    private void Update()
    {
        RefreshPermissionState();

        if (!hasPermission)
        {
            status = "Activity Recognition permission is needed for steps.";
            return;
        }

        StartStepDetectorIfPossible();
        TryEnableStepCounter();
        UpdateDelayedStepCounter();

        var detectorSteps = ReadDetectedStepEvents();
        if (detectorSteps != lastShownDetectorSteps)
        {
            lastShownDetectorSteps = detectorSteps;
            lastDetectorStepTime = Time.realtimeSinceStartup;
        }

        ProcessNewDetectorEvents(detectorSteps);

        if (stepDetectorRunning)
        {
            sessionSteps = acceptedDetectorSteps;
            status = "Using filtered TYPE_STEP_DETECTOR events.";
            return;
        }

        if (stepCounter == null)
        {
            status = "No TYPE_STEP_DETECTOR or StepCounter detected on this device.";
            return;
        }

        sessionSteps = Mathf.Max(0, rawStepCount - startingStepCount);
        status = stepDetectorAvailable
            ? "TYPE_STEP_DETECTOR found but did not start; using delayed StepCounter."
            : "TYPE_STEP_DETECTOR missing; using delayed StepCounter.";
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            RequestPermissionIfNeeded();
            StartStepDetectorIfPossible();
            TryEnableStepCounter();
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            StopStepDetector();
            return;
        }

        RequestPermissionIfNeeded();
        StartStepDetectorIfPossible();
        TryEnableStepCounter();
    }

    private void UpdateDelayedStepCounter()
    {
        if (stepCounter == null)
        {
            return;
        }

        rawStepCount = stepCounter.stepCounter.ReadValue();

        if (!hasFirstReading)
        {
            startingStepCount = rawStepCount;
            lastRawStepCount = rawStepCount;
            lastRawChangeTime = Time.realtimeSinceStartup;
            hasFirstReading = true;
        }

        if (rawStepCount != lastRawStepCount)
        {
            lastRawStepCount = rawStepCount;
            lastRawChangeTime = Time.realtimeSinceStartup;
        }
    }

    private void ProcessNewDetectorEvents(int rawDetectorEvents)
    {
        var newEvents = rawDetectorEvents - processedDetectorEvents;
        if (newEvents <= 0)
        {
            return;
        }

        processedDetectorEvents = rawDetectorEvents;

        for (var i = 0; i < newEvents; i++)
        {
            TryAcceptDetectorStep(Time.realtimeSinceStartup);
        }
    }

    private void TryAcceptDetectorStep(float now)
    {
        if (lastCandidateStepTime < 0f)
        {
            lastCandidateStepTime = now;
            rhythmEventCount = 1;
            detectorFilterStatus = "First movement seen. Waiting for another step-like event.";
            return;
        }

        var secondsSinceLastCandidate = now - lastCandidateStepTime;
        lastCandidateStepTime = now;

        if (secondsSinceLastCandidate < MinSecondsBetweenSteps)
        {
            rejectedDetectorEvents++;
            rhythmEventCount = 0;
            detectorFilterStatus = $"Ignored: too fast ({secondsSinceLastCandidate:0.00}s).";
            return;
        }

        if (secondsSinceLastCandidate > MaxSecondsBetweenSteps)
        {
            rhythmEventCount = 1;
            detectorFilterStatus = "New movement seen. Waiting for steady walking rhythm.";
            return;
        }

        rhythmEventCount = Mathf.Min(rhythmEventCount + 1, RequiredRhythmEvents);

        if (rhythmEventCount < RequiredRhythmEvents)
        {
            detectorFilterStatus = "Movement seen, but rhythm is not confirmed yet.";
            return;
        }

        acceptedDetectorSteps++;
        detectorFilterStatus = $"Accepted step ({secondsSinceLastCandidate:0.00}s cadence).";
    }

    private void TryEnableStepCounter()
    {
        stepCounter = StepCounter.current ?? InputSystem.GetDevice<StepCounter>();

        if (stepCounter == null)
        {
            status = "No StepCounter detected yet. Use a real Android phone.";
            return;
        }

        if (!stepCounter.enabled)
        {
            InputSystem.EnableDevice(stepCounter);
        }

        stepCounter.MakeCurrent();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void StartStepDetectorIfPossible()
    {
        if (!hasPermission || stepDetectorRunning)
        {
            return;
        }

        try
        {
            if (sensorManager == null)
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    sensorManager = activity.Call<AndroidJavaObject>("getSystemService", "sensor");
                }
            }

            if (stepDetectorSensor == null && !checkedStepDetector)
            {
                checkedStepDetector = true;

                using (var sensorClass = new AndroidJavaClass("android.hardware.Sensor"))
                {
                    var sensorTypeStepDetector = sensorClass.GetStatic<int>("TYPE_STEP_DETECTOR");
                    stepDetectorSensor = sensorManager.Call<AndroidJavaObject>("getDefaultSensor", sensorTypeStepDetector);
                }

                stepDetectorAvailable = stepDetectorSensor != null;
            }

            if (stepDetectorSensor == null)
            {
                stepDetectorAvailable = false;
                return;
            }

            if (stepDetectorListener == null)
            {
                stepDetectorListener = new StepDetectorListener(this);
            }

            using (var sensorManagerClass = new AndroidJavaClass("android.hardware.SensorManager"))
            {
                var delay = sensorManagerClass.GetStatic<int>("SENSOR_DELAY_NORMAL");
                stepDetectorRunning = sensorManager.Call<bool>("registerListener", stepDetectorListener, stepDetectorSensor, delay);
            }
        }
        catch (System.Exception exception)
        {
            stepDetectorRunning = false;
            status = "TYPE_STEP_DETECTOR error: " + exception.Message;
        }
    }

    private void StopStepDetector()
    {
        if (sensorManager != null && stepDetectorListener != null)
        {
            sensorManager.Call("unregisterListener", stepDetectorListener);
        }

        stepDetectorRunning = false;
    }

    private void OnAndroidStepDetected()
    {
        Interlocked.Increment(ref detectedStepEvents);
    }

    private int ReadDetectedStepEvents()
    {
        return Interlocked.CompareExchange(ref detectedStepEvents, 0, 0);
    }

    private sealed class StepDetectorListener : AndroidJavaProxy
    {
        private readonly StepCounterDisplay owner;

        public StepDetectorListener(StepCounterDisplay owner)
            : base("android.hardware.SensorEventListener")
        {
            this.owner = owner;
        }

        public void onSensorChanged(AndroidJavaObject sensorEvent)
        {
            owner.OnAndroidStepDetected();
        }

        public void onAccuracyChanged(AndroidJavaObject sensor, int accuracy)
        {
        }
    }
#else
    private void StartStepDetectorIfPossible()
    {
        checkedStepDetector = true;
        stepDetectorAvailable = false;
        stepDetectorRunning = false;
    }

    private void StopStepDetector()
    {
    }

    private int ReadDetectedStepEvents()
    {
        return 0;
    }
#endif

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is StepCounter)
        {
            TryEnableStepCounter();
        }
    }

    private void RequestPermissionIfNeeded()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        RefreshPermissionState();

        if (!needsActivityRecognitionPermission)
        {
            status = "No runtime step permission needed on this Android version.";
            return;
        }

        if (hasPermission)
        {
            status = "Activity Recognition permission already granted.";
            return;
        }

        if (requestedPermission)
        {
            return;
        }

        requestedPermission = true;

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ =>
        {
            hasPermission = true;
            status = "Permission granted. Starting step sensors...";
            StartStepDetectorIfPossible();
            TryEnableStepCounter();
        };
        callbacks.PermissionDenied += _ =>
        {
            hasPermission = false;
            status = "Permission denied. Enable Physical activity in Android app settings.";
        };

        Permission.RequestUserPermission(ActivityRecognitionPermission, callbacks);
        status = "Waiting for Android Physical activity permission...";
#else
        hasPermission = true;
        status = "Build to a physical Android phone to read real steps.";
#endif
    }

    private void RefreshPermissionState()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        hasPermission = !needsActivityRecognitionPermission ||
            Permission.HasUserAuthorizedPermission(ActivityRecognitionPermission);
#else
        hasPermission = true;
#endif
    }

    private bool NeedsActivityRecognitionPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            return versionClass.GetStatic<int>("SDK_INT") >= AndroidQApiLevel;
        }
#else
        return false;
#endif
    }

    private void OnGUI()
    {
        var scale = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 160f : Screen.width / 390f);
        var margin = Mathf.RoundToInt(24f * scale);
        var width = Screen.width - (margin * 2);

        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(30f * scale),
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        titleStyle.normal.textColor = Color.white;

        var bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(18f * scale),
            wordWrap = true
        };
        bodyStyle.normal.textColor = Color.white;

        var stepCounterState = stepCounter == null
            ? "missing"
            : $"{stepCounter.displayName} / enabled: {stepCounter.enabled}";
        var secondsSinceRawChange = hasFirstReading ? Time.realtimeSinceStartup - lastRawChangeTime : 0f;
        var detectorStepTime = lastDetectorStepTime >= 0f
            ? $"{Time.realtimeSinceStartup - lastDetectorStepTime:0.0}s ago"
            : "none yet";
        var stepDetectorState = stepDetectorAvailable
            ? $"available / running: {stepDetectorRunning}"
            : checkedStepDetector ? "missing" : "checking";

        GUI.Label(new Rect(margin, margin, width, 54f * scale), $"Session Steps: {sessionSteps}", titleStyle);
        GUI.Label(new Rect(margin, margin + (60f * scale), width, 250f * scale),
            $"TYPE_STEP_DETECTOR: {stepDetectorState}\nRaw Detector Events: {lastShownDetectorSteps}\nAccepted: {acceptedDetectorSteps}\nRejected: {rejectedDetectorEvents}\nDetector changed: {detectorStepTime}\nRaw StepCounter: {rawStepCount}\nPermission: {hasPermission}\n{detectorFilterStatus}\n{status}",
            bodyStyle);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!hasPermission && GUI.Button(new Rect(margin, margin + (320f * scale), width, 48f * scale), "Request Physical Activity Permission"))
        {
            requestedPermission = false;
            RequestPermissionIfNeeded();
        }
#endif
    }
}
