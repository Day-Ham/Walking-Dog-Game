using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
public class StepCountTracker : MonoBehaviour
{
    //input action control
    [SerializeField] private InputActionAsset controls;
    [SerializeField] private string mapName;
    private InputAction stepCountAction;

    [SerializeField]
    private int stepCount = 0;

    [SerializeField]
    public TMPro.TextMeshProUGUI stepCountText;

    private int initialStepCount = 0;


    //for ANDROID


    void Start()
    {
        stepCountAction = controls.FindActionMap(mapName).FindAction("StepCount");

        if (Gamepad.current != null)
            Debug.Log("Connected");
        else Debug.Log("Error: No Gamepad");

        stepCountAction.Enable();
        if(stepCountAction.enabled)
            Debug.Log("StepCount Action Enabled");
        else
            Debug.Log("StepCount Action Not Enabled");

        if (StepCounter.current != null)
        {
            InputSystem.EnableDevice(StepCounter.current);
            Debug.Log("Step Counter detected!");
        }

        initialStepCount = stepCountAction.ReadValue<int>();
    }

    // Update is called once per frame
    void Update()
    {
      int rawCount = stepCountAction.ReadValue<int>();

        stepCount = rawCount;

        stepCountText.text = stepCount.ToString();
    }
}
