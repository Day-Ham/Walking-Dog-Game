using TMPro;
using UnityEngine;

public class GPSUIController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField]
    TextMeshProUGUI longText;
    [SerializeField]
    TextMeshProUGUI latText;
    void Start()
    {
        longText = GameObject.Find("Longitude").GetComponent<TextMeshProUGUI>();
        latText = GameObject.Find("Latitude").GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        longText.text = "Longitude: " + StepCountAndGpsManager.Instance.getLongitude().ToString();
        latText.text = "Latitude: " + StepCountAndGpsManager.Instance.getLatitude().ToString();
    }
}
