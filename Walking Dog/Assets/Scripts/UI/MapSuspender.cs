using System.Collections.Generic;
using UnityEngine;

public class MapSuspender : MonoBehaviour
{
    private readonly List<OpenFreeMapWebViewMap> hiddenMaps = new List<OpenFreeMapWebViewMap>();

    private void OnEnable()
    {
        hiddenMaps.Clear();
        // Use the modern API to avoid warnings
        foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (map.enabled)
            {
                map.Suspend(this);
                hiddenMaps.Add(map);
            }
        }
    }

    private void OnDisable()
    {
        foreach (var map in hiddenMaps)
        {
            if (map != null) map.Resume(this);
        }
        hiddenMaps.Clear();
    }
}
