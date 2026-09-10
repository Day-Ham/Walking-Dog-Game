using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class WalkDataTab : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] private TMP_Text dateText;
    [SerializeField] private TMP_Text measurementsText;
    [SerializeField] private Button selectButton;

    public void SetData(string date, string measurements, UnityAction onSelected)
    {
        dateText.text = date;
        measurementsText.text = measurements;

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(onSelected);
    }
}
