using TMPro;
using UnityEngine;

public class UIMilestoneNotifier : MonoBehaviour
{
    [Tooltip("The red dot or badge graphic to show when a free roll is available.")]
    public GameObject badgeGraphic;

    [Tooltip("Optional text element to show the exact number of free rolls available (e.g. '1 Free Roll!').")]
    public TMP_Text alertText;
    
    [Tooltip("What the text should say when a roll is available.")]
    public string singleRollText = "1 Free Roll!";
    public string multipleRollsText = "{0} Free Rolls!";

    private void Update()
    {
        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();

        if (bootstrap != null && bootstrap.Wallet != null && bootstrap.Wallet.Snapshot != null)
        {
            long totalFreeRolls = PointsWalletSnapshot.MilestoneRollsForEarnedPoints(bootstrap.Wallet.Snapshot.TotalEarned);
            long claimed = bootstrap.Wallet.Snapshot.MilestoneRollsClaimed;
            long availableRolls = totalFreeRolls - claimed;

            // This is a phone notification created on this device, not a Firebase push.
            // The helper remembers each account's last alerted milestone, so Update() cannot
            // create the same notification every frame.
            LocalPhoneNotifications.NotifyNewMilestone(bootstrap.Wallet.Owner, totalFreeRolls, availableRolls);

            bool hasRolls = availableRolls > 0;

            if (badgeGraphic != null)
            {
                badgeGraphic.SetActive(hasRolls);
            }

            if (alertText != null)
            {
                alertText.gameObject.SetActive(hasRolls);
                if (hasRolls)
                {
                    alertText.text = availableRolls == 1 ? singleRollText : string.Format(multipleRollsText, availableRolls);
                }
            }
        }
        else
        {
            if (badgeGraphic != null) badgeGraphic.SetActive(false);
            if (alertText != null) alertText.gameObject.SetActive(false);
        }
    }
}
