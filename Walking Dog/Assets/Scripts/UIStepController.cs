using TMPro;
using UnityEngine;

public class UIStepController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stepText;
    [SerializeField] private string prefix = "";
    [SerializeField] private bool showWalkingSessionSteps = false;
    [SerializeField] private bool displayAsPoints = true;

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
            
        int valueToDisplay = displayAsPoints ? steps / 10 : steps;
        stepText.text = string.IsNullOrEmpty(prefix) ? valueToDisplay.ToString() : $"{prefix}{valueToDisplay}";
    }
}
