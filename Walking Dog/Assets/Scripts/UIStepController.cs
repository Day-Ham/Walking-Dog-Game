using TMPro;
using UnityEngine;

public class UIStepController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stepText;
    [SerializeField] private string prefix = "";
    [SerializeField] private bool showWalkingSessionSteps = false;
   
    
  //  [SerializeField] private bool displayAsPoints = true;

    private void Awake()
    {
        if (stepText == null)
        {
            stepText = GetComponent<TextMeshProUGUI>();
        }
    }

    private void LateUpdate()
    {
        if (stepText == null)
        {
            return;
        }

        var manager = StepCountAndGpsManager.Instance;
        var steps = manager == null
            ? 0
            : showWalkingSessionSteps ? manager.WalkingSessionSteps : manager.Steps;
            
        //int valueToDisplay = displayAsPoints ? steps / 10 : steps;
      
        // temporary setpoint here in order for it to use manager, will figure out where to put the calculation of points later -ram
        manager.setPoint(steps / 10);
       
        stepText.text = string.IsNullOrEmpty(prefix) ? manager.getPoint().ToString() : $"{prefix}{manager.getPoint()}";
    }
}
