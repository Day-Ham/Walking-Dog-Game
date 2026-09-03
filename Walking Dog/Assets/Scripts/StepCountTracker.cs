using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;


#if UNITY_ANDROID
using UnityEngine.Android;
#endif
public class StepCountTracker : MonoBehaviour
{
    //input action control


    [SerializeField]
    private int stepCount = 0;

    [SerializeField]
    public TMPro.TextMeshProUGUI stepCountText;

    [SerializeField]
    public TMPro.TextMeshProUGUI StatusText;

    private int initialStepCount = 0;
    private int sessionBaseSteps = -1;


    //for ANDROID


    void Start()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission("android.permission.ACTIVITY_RECOGNITION"))
        {
            Permission.RequestUserPermission("android.permission.ACTIVITY_RECOGNITION");
        }
#endif



        if (StepCounter.current != null)
        {
            InputSystem.EnableDevice(StepCounter.current);
            Debug.Log("Step Counter detected!");
            StatusText.text = "Step Counter detected!";
        }
        {
            Debug.LogWarning("No Step Counter hardware found on this device.");
            if (stepCountText != null)
            {
                stepCountText.text = "No Step Counter Found";
            }
        }


    }

    // Update is called once per frame
    void Update()
    {

        int StartStepCount = StepCounter.current.stepCounter.ReadValue();

        //set startingBase
        if (sessionBaseSteps == -1 && StartStepCount >= 0)
        {
            sessionBaseSteps = StartStepCount;
        }


        if (sessionBaseSteps != -1)
        {
            stepCount = StartStepCount - sessionBaseSteps;

            // Update UI
            if (stepCountText != null)
            {
                stepCountText.text = "Steps: " + stepCount.ToString();
            }

        }
    }
}









