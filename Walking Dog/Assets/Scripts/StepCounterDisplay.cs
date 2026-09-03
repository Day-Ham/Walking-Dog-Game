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
    private const float MinWalkingMotion = 0.07f;
    private const float StillMotionThreshold = 0.025f;
    private const float MaxShakeJerk = 8.0f;
    private const float MinWalkingConfidence = 0.35f;
    private const float CounterCorrectionGraceSeconds = 12f;
    private const int MaxDetectorLeadBeforeCorrection = 6;

    private StepCounter stepCounter;
    private Accelerometer accelerometer;
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
    private bool accelerometerAvailable;
    private bool hasAccelerationReading;
    private bool hasFirstReading;
    private int startingStepCount;
    private int rawStepCount;
    private int lastRawStepCount;
    private int delayedCounterSessionSteps;
    private int sessionSteps;
    private int acceptedDetectorSteps;
    private int pendingDetectorSteps;
    private int processedDetectorEvents;
    private int rejectedDetectorEvents;
    private int counterUpCorrections;
    private int counterDownCorrections;
    private int lastShownDetectorSteps;
    private int rhythmEventCount;
    private Vector3 smoothedAcceleration;
    private Vector3 previousAcceleration;
    private float motionLevel;
    private float shakeLevel;
    private float walkingConfidence;
    private float stillSeconds;
    private float lastRawChangeTime;
    private float lastCandidateStepTime = -1f;
    private float lastDetectorStepTime = -1f;
    private string detectorFilterStatus = "Waiting for walking rhythm...";
    private string motionFilterStatus = "Motion filter starting...";
    private string status = "Starting step counter...";

    public int SessionSteps => sessionSteps;
    public string Status => status;
    public string DetectorFilterStatus => detectorFilterStatus;
    public string MotionFilterStatus => motionFilterStatus;
    public bool CheckedStepDetector => checkedStepDetector;
    public bool IsStepDetectorRunning => stepDetectorRunning;

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
        TryEnableMotionSensors();
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
        TryEnableMotionSensors();
        UpdateMotionFilter();
        UpdateDelayedStepCounter();

        var detectorSteps = ReadDetectedStepEvents();
        if (detectorSteps != lastShownDetectorSteps)
        {
            lastShownDetectorSteps = detectorSteps;
            lastDetectorStepTime = Time.realtimeSinceStartup;
        }

        ProcessNewDetectorEvents(detectorSteps);
        ApplyStepCounterCorrection();

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
            TryEnableMotionSensors();
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
        TryEnableMotionSensors();
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
            // Android's TYPE_STEP_COUNTER may read as zero until its first
            // sensor event, then report its cumulative count since boot. Do
            // not use that placeholder zero as this app session's baseline.
            if (rawStepCount == 0)
            {
                return;
            }

            startingStepCount = rawStepCount;
            lastRawStepCount = rawStepCount;
            lastRawChangeTime = Time.realtimeSinceStartup;
            hasFirstReading = true;
            delayedCounterSessionSteps = 0;
            return;
        }

        if (rawStepCount != lastRawStepCount)
        {
            lastRawStepCount = rawStepCount;
            lastRawChangeTime = Time.realtimeSinceStartup;
        }

        delayedCounterSessionSteps = Mathf.Max(0, rawStepCount - startingStepCount);
    }

    private void TryEnableMotionSensors()
    {
        accelerometer = Accelerometer.current ?? InputSystem.GetDevice<Accelerometer>();

        if (accelerometer == null)
        {
            accelerometerAvailable = false;
            motionFilterStatus = "Accelerometer missing; using rhythm filter only.";
            return;
        }

        accelerometerAvailable = true;

        if (!accelerometer.enabled)
        {
            InputSystem.EnableDevice(accelerometer);
        }

        accelerometer.MakeCurrent();
    }

    private void UpdateMotionFilter()
    {
        if (accelerometer == null)
        {
            return;
        }

        var acceleration = accelerometer.acceleration.ReadValue();
        var deltaTime = Mathf.Max(Time.deltaTime, 0.001f);

        if (!hasAccelerationReading)
        {
            smoothedAcceleration = acceleration;
            previousAcceleration = acceleration;
            hasAccelerationReading = true;
            motionFilterStatus = "Motion filter ready.";
            return;
        }

        var gravityBlend = 1f - Mathf.Exp(-deltaTime * 6f);
        smoothedAcceleration = Vector3.Lerp(smoothedAcceleration, acceleration, gravityBlend);

        var motion = (acceleration - smoothedAcceleration).magnitude;
        var jerk = (acceleration - previousAcceleration).magnitude / deltaTime;
        var levelBlend = 1f - Mathf.Exp(-deltaTime * 8f);

        motionLevel = Mathf.Lerp(motionLevel, motion, levelBlend);
        shakeLevel = Mathf.Lerp(shakeLevel, jerk, levelBlend);
        previousAcceleration = acceleration;

        if (motionLevel < StillMotionThreshold)
        {
            stillSeconds += deltaTime;
        }
        else
        {
            stillSeconds = 0f;
        }

        var motionLooksWalkable = motionLevel >= MinWalkingMotion && shakeLevel <= MaxShakeJerk;
        var targetConfidence = motionLooksWalkable ? 1f : 0f;
        var confidenceSpeed = motionLooksWalkable ? 2.0f : 1.5f;
        walkingConfidence = Mathf.MoveTowards(walkingConfidence, targetConfidence, deltaTime * confidenceSpeed);

        if (shakeLevel > MaxShakeJerk)
        {
            motionFilterStatus = $"Too shaky ({shakeLevel:0.0}); rejecting step events.";
            return;
        }

        if (walkingConfidence >= MinWalkingConfidence)
        {
            motionFilterStatus = $"Walking-like motion ({walkingConfidence * 100f:0}% confidence).";
            return;
        }

        motionFilterStatus = $"Low walking confidence ({walkingConfidence * 100f:0}%).";
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
            pendingDetectorSteps = 1;
            detectorFilterStatus = "First movement seen. Waiting for another step-like event.";
            return;
        }

        var secondsSinceLastCandidate = now - lastCandidateStepTime;
        lastCandidateStepTime = now;

        if (!MotionLooksLikeWalking())
        {
            rejectedDetectorEvents++;
            rhythmEventCount = 0;
            pendingDetectorSteps = 0;
            detectorFilterStatus = "Ignored: motion does not look like steady walking.";
            return;
        }

        if (stillSeconds > 1.0f)
        {
            rejectedDetectorEvents++;
            rhythmEventCount = 0;
            pendingDetectorSteps = 0;
            detectorFilterStatus = "Ignored: phone looked still before this event.";
            return;
        }

        if (secondsSinceLastCandidate < MinSecondsBetweenSteps)
        {
            rejectedDetectorEvents++;
            rhythmEventCount = 0;
            pendingDetectorSteps = 0;
            detectorFilterStatus = $"Ignored: too fast ({secondsSinceLastCandidate:0.00}s).";
            return;
        }

        if (secondsSinceLastCandidate > MaxSecondsBetweenSteps)
        {
            rhythmEventCount = 1;
            pendingDetectorSteps = 1;
            detectorFilterStatus = "New movement seen. Waiting for steady walking rhythm.";
            return;
        }

        rhythmEventCount = Mathf.Min(rhythmEventCount + 1, RequiredRhythmEvents);
        pendingDetectorSteps++;

        if (rhythmEventCount < RequiredRhythmEvents)
        {
            detectorFilterStatus = "Movement seen, but rhythm is not confirmed yet.";
            return;
        }

        acceptedDetectorSteps += Mathf.Max(1, pendingDetectorSteps);
        pendingDetectorSteps = 0;
        detectorFilterStatus = $"Accepted step ({secondsSinceLastCandidate:0.00}s cadence).";
    }

    private bool MotionLooksLikeWalking()
    {
        if (!accelerometerAvailable)
        {
            return true;
        }

        return walkingConfidence >= MinWalkingConfidence && shakeLevel <= MaxShakeJerk;
    }

    private void ApplyStepCounterCorrection()
    {
        if (!hasFirstReading)
        {
            return;
        }

        if (delayedCounterSessionSteps > acceptedDetectorSteps)
        {
            counterUpCorrections += delayedCounterSessionSteps - acceptedDetectorSteps;
            acceptedDetectorSteps = delayedCounterSessionSteps;
            pendingDetectorSteps = 0;
            detectorFilterStatus = "Corrected upward from delayed StepCounter.";
            return;
        }

        var detectorLead = acceptedDetectorSteps - delayedCounterSessionSteps;
        var rawCounterDelay = Time.realtimeSinceStartup - lastRawChangeTime;
        if (detectorLead <= MaxDetectorLeadBeforeCorrection || rawCounterDelay < CounterCorrectionGraceSeconds)
        {
            return;
        }

        var correctedSteps = delayedCounterSessionSteps + MaxDetectorLeadBeforeCorrection;
        counterDownCorrections += acceptedDetectorSteps - correctedSteps;
        acceptedDetectorSteps = correctedSteps;
        pendingDetectorSteps = 0;
        rhythmEventCount = 0;
        detectorFilterStatus = "Corrected downward: detector was far ahead of delayed StepCounter.";
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

        if (device is Accelerometer)
        {
            TryEnableMotionSensors();
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


    private void LateUpdate()
    {
        var manager = StepCountAndGpsManager.Instance;

        if (manager != null)
        {
            manager.SetStep(sessionSteps);
        }
    }
}

