using TMPro;
using UnityEngine;

public class UIStepController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stepText;
    [SerializeField] private string prefix = "";
    [SerializeField] private bool showWalkingSessionSteps = false;

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
        var points = manager == null
            ? 0
            : showWalkingSessionSteps ? manager.WalkingSessionPoints : manager.StepPoints;
        stepText.text = string.IsNullOrEmpty(prefix) ? points.ToString() : $"{prefix}{points}";
    }
}
