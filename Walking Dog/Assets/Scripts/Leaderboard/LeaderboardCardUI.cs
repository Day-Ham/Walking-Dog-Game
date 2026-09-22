using TMPro;
using UnityEngine;

namespace WalkingDog.Leaderboards
{
    public sealed class LeaderboardCardUI : MonoBehaviour
    {

        // UI elements for displaying leaderboard entry information in the card
        [SerializeField] private TMP_Text playerName;
        [SerializeField] private TMP_Text details;
        [SerializeField] private ProfilePhotoUI profilePhoto;

        public void Bind(LeaderboardEntry entry, int position, bool isCurrentPlayer, LeaderboardMetric metric) // connects the leaderboard entry data to the UI elements
        {
            if (profilePhoto == null)
            {
                var slot = transform.Find("Image");
                if (slot != null) profilePhoto = slot.GetComponent<ProfilePhotoUI>() ?? slot.gameObject.AddComponent<ProfilePhotoUI>();
            }
            if (profilePhoto != null) profilePhoto.Bind(entry.PlayerId, entry.DisplayName, entry.PhotoUrl, playerName.font);
            playerName.richText = false;
            playerName.text = entry.DisplayName;
            details.richText = false;
            string distance = $"{entry.TotalDistanceMeters / 1000d:N2} km";
            string steps = $"{entry.TotalSteps:N0} steps";
            details.text = $"#{position}{(isCurrentPlayer ? " · YOU" : "")}\n"
                + (metric == LeaderboardMetric.Distance ? distance + "\n" + steps : steps + "\n" + distance)
                + $"\n{entry.CompletedWalkCount:N0} walks";
            var color = isCurrentPlayer ? new Color(1f, 0.85f, 0.35f) : Color.white;
            playerName.color = details.color = color;
        }
    }
}
