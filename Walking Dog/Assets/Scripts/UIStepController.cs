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
        var value = showWalkingSessionSteps
            ? (manager == null ? "0" : manager.WalkingSessionPoints.ToString())
            : manager?.GetComponent<FirebaseWalkBootstrap>()?.Wallet?.DisplayText ?? "Points unavailable";
        stepText.text = string.IsNullOrEmpty(prefix) ? value : $"{prefix}{value}";
    }
}
