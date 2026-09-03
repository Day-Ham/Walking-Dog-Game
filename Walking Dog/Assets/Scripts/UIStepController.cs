using UnityEngine;
using TMPro;
public class UIStepController : MonoBehaviour
{

    [SerializeField]
    private TextMeshProUGUI stepText;

    void Start()
    {
       stepText = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    private void LateUpdate()
    {
        var manager = StepCountAndGpsManager.Instance;

        if (manager != null)
        {
            stepText.text = StepCountAndGpsManager.Instance.getSteps().ToString();
        }
    }
    
}
