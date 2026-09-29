using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class MapSignalOverlay : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    private MapSuspender mapSuspender;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        mapSuspender = GetComponent<MapSuspender>();
    }

    private void Update()
    {
        var manager = StepCountAndGpsManager.Instance;
        
        // If there's no manager, hide the overlay.
        bool needsGps = manager != null && !manager.HasFreshLocation;

        // 1. Toggle visibility using CanvasGroup so this GameObject never gets disabled
        // (If the GameObject gets disabled, this Update loop would stop running forever!)
        if (canvasGroup != null)
        {
            canvasGroup.alpha = needsGps ? 1f : 0f;
            canvasGroup.interactable = needsGps;
            canvasGroup.blocksRaycasts = needsGps;
        }

        // 2. If there's a MapSuspender attached, enable/disable it to hide the Native Android map
        if (mapSuspender != null && mapSuspender.enabled != needsGps)
        {
            mapSuspender.enabled = needsGps;
        }
    }
}
