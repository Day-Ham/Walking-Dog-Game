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
    }
}
