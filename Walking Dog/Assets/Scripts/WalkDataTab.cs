using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class WalkDataTab : MonoBehaviour
{
    [Header("Entry content")]
    [Tooltip("The title shown for the date and time of this walk.")]
    [SerializeField] private TMP_Text dateText;
    [Tooltip("The steps, distance, and duration line.")]
    [SerializeField] private TMP_Text measurementsText;
    [Header("Interaction")]
    [Tooltip("The whole row button that opens the recorded route.")]
    [SerializeField] private Button selectButton;

    // Binds the date string, measurement metrics, and selection callback to the walk entry UI row.
    public void SetData(string date, string measurements, UnityAction onSelected)
    {
        dateText.text = date;
        measurementsText.text = measurements;

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(onSelected);
    }
}
