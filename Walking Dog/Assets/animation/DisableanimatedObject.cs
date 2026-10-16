using System.Collections;
using UnityEngine;

public class DisableanimatedObject : MonoBehaviour
{
    [SerializeField] private GameObject disableTarget;
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
        var target = disableTarget != null ? disableTarget : gameObject;
        // The optional receiver lives on a parent Canvas. SendMessageUpwards
        // avoids tying this reusable helper to the dog-game assembly. It runs
        // immediately before deactivation so parent cleanup is guaranteed even
        // when this component is disabled along with its target.
        target.SendMessageUpwards("NotifyScreenDisabled", target, SendMessageOptions.DontRequireReceiver);
        target.SetActive(false);
        disableScheduled = false;
    }
}
