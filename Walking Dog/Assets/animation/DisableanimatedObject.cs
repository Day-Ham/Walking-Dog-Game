using System.Collections;
using UnityEngine;

public class DisableanimatedObject : MonoBehaviour
{
    private bool disableScheduled;

    // Called by the LeaderboardExit animation event. Disabling during that
    // callback makes TMP_InputField dispose its generated mesh immediately,
    // which Unity does not permit in an animation-event callback.
    public void setActiveFalse()
    {
        if (disableScheduled || !gameObject.activeSelf) return;
        disableScheduled = true;
        StartCoroutine(DisableNextFrame());
    }

    private IEnumerator DisableNextFrame()
    {
        yield return null;
        gameObject.SetActive(false);
        disableScheduled = false;
    }
}
