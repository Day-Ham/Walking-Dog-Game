using TMPro;
using UnityEngine;

public class UIStepController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stepText;
    [SerializeField] private string prefix = "";
    [SerializeField] private bool showWalkingSessionSteps = false;

    // Caches TextMeshProUGUI component if not assigned via Inspector.
    private void Awake()
    {
        if (stepText == null)
        {
            stepText = GetComponent<TextMeshProUGUI>();
        }
    }

    // Updates step or points text display each frame based on active session or wallet state.
    private void LateUpdate()
    {
        if (stepText == null)
        {
            return;
        }

        var manager = StepCountAndGpsManager.Instance;
        string value;

        if (showWalkingSessionSteps)
        {
            if (manager == null)
            {
                value = "0";
            }
            else
            {
                value = manager.WalkingSessionPoints.ToString();
            }
        }
        else
        {
            var firebaseWalk = manager?.GetComponent<FirebaseWalkBootstrap>();
            var wallet = firebaseWalk?.Wallet;

            if (wallet != null)
            {
                value = wallet.DisplayText;
            }
            else
            {
                value = "Points unavailable";
            }
        }
        stepText.text = prefix + value;
    }
}
