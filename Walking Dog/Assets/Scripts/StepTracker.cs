using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class StepTracker : MonoBehaviour
{
    [Tooltip("UI Text element to display the step count")]
    public Text stepCountText;
    
    private int sessionBaselineSteps = -1;
    private int currentSessionSteps = 0;

    void Start()
    {
        //Request Permission on Android for API 29+
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission("android.permission.ACTIVITY_RECOGNITION"))
        {
            Permission.RequestUserPermission("android.permission.ACTIVITY_RECOGNITION");
        }
#endif

        //Enable the Step Counter Device
        if (StepCounter.current != null)
        {
            InputSystem.EnableDevice(StepCounter.current);
            Debug.Log("Step Counter found and enabled.");
        }
        else
        {
            Debug.LogWarning("No Step Counter hardware found on this device.");
            if (stepCountText != null)
            {
                stepCountText.text = "No Step Counter Found";
            }
        }
    }

    void Update()
    {
        //Read steps if the device is present and active
        if (StepCounter.current != null)
        {
            int hardwareSteps = StepCounter.current.stepCounter.ReadValue();
            
            // Set baseline 
            if (sessionBaselineSteps == -1 && hardwareSteps >= 0)
            {
                sessionBaselineSteps = hardwareSteps;
            }

            if (sessionBaselineSteps != -1)
            {
                currentSessionSteps = hardwareSteps - sessionBaselineSteps;
                
                // Update UI
                if (stepCountText != null)
                {
                    stepCountText.text = "Steps: " + currentSessionSteps.ToString();
                }
            }
        }
    }
}
