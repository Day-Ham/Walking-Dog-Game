using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WalkingDog.Leaderboards
{
    public sealed class SocialCardUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text playerName, details;
        [SerializeField] private ProfilePhotoUI photo;
        [SerializeField] private Button follow, view;

        internal void Bind(SocialPlayer player, string viewer, SocialActivity activity,
            Action<SocialPlayer> changeFollow, Action<SocialPlayer> openProfile,
            Action<SocialActivity> openWalk, bool viewingProfile)
        {
            playerName.text = player.Name + (player.Id == viewer ? " · You" : "");
            photo.Bind(player.Id, player.Name, player.Photo, playerName.font);
            details.text = activity == null
                ? (player.FollowsYou ? "Follows you · " : "") + (player.SharesActivity ? "Shares walks with followers" : "Walk sharing is off")
                : $"{activity.EndedUtc.ToLocalTime():MMM d, yyyy · h:mm tt}\n{activity.DistanceMeters / 1000d:N2} km   ·   {activity.Steps:N0} steps   ·   {activity.DurationSeconds / 60d:N0} min";
            follow.gameObject.SetActive(player.Id != viewer && activity == null);
            follow.GetComponentInChildren<TMP_Text>().text = player.Following ? "Unfollow" : player.FollowsYou ? "Follow back" : "Follow";
            follow.onClick.RemoveAllListeners();
            follow.onClick.AddListener(() => changeFollow(player));
            view.onClick.RemoveAllListeners();
            view.onClick.AddListener(() => { if (activity != null) openWalk(activity); else openProfile(player); });
            view.interactable = activity != null || !viewingProfile;
            view.GetComponentInChildren<TMP_Text>().text = activity != null ? "View details"
                : viewingProfile ? "Viewing walks" : "View walks";
        }
    }
}
