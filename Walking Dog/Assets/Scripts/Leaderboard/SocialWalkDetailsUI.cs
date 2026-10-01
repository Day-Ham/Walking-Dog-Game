using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WalkingDog.Leaderboards
{
    // Built on first use from the existing screen's font/button style. Existing
    // scenes gain the popup without rerunning the scene migration.
    public sealed class SocialWalkDetailsUI : MonoBehaviour
    {
        private TMP_Text playerName, date, distance, steps, duration;
        private ProfilePhotoUI photo;
        private Button close;
        private Button shareRoute;
        private TMP_Text routeStatus;
        private OpenFreeMapWebViewMap map;
        private GameObject mapArea;
        private GameObject previousSelection;

        internal static SocialWalkDetailsUI Create(Transform parent, Button buttonSource,
            TMP_FontAsset font, Action onClose, Action onShareRoute)
        {
            var root = new GameObject("Walk details popup", typeof(RectTransform), typeof(Image));
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            FriendsPanelUI.Place(root.transform, 0, 0, 1, 1);
            root.GetComponent<Image>().color = new Color(0, 0, 0, .7f);
            var popup = root.AddComponent<SocialWalkDetailsUI>();
            var panel = new GameObject("Walk details", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            FriendsPanelUI.Place(panel.transform, .07f, .08f, .93f, .92f);
            panel.GetComponent<Image>().color = new Color(.04f, .28f, .32f);
            Label(panel.transform, font, "Title", "Walk details", .07f, .91f, .93f, .98f, 46);
            var portrait = new GameObject("Walker photo", typeof(RectTransform), typeof(Image), typeof(ProfilePhotoUI));
            portrait.transform.SetParent(panel.transform, false);
            FriendsPanelUI.Place(portrait.transform, .07f, .795f, .23f, .90f);
            popup.photo = portrait.GetComponent<ProfilePhotoUI>();
            popup.playerName = Label(panel.transform, font, "Walker", "", .28f, .845f, .93f, .90f, 36);
            popup.date = Label(panel.transform, font, "Finished", "", .28f, .79f, .93f, .84f, 27);
            popup.distance = Label(panel.transform, font, "Distance", "", .07f, .725f, .93f, .78f, 31);
            popup.steps = Label(panel.transform, font, "Steps", "", .07f, .67f, .93f, .72f, 31);
            popup.duration = Label(panel.transform, font, "Duration", "", .07f, .615f, .93f, .665f, 31);
            popup.mapArea = new GameObject("Session route map", typeof(RectTransform), typeof(Image));
            popup.mapArea.transform.SetParent(panel.transform, false);
            FriendsPanelUI.Place(popup.mapArea.transform, .07f, .255f, .93f, .60f);
            popup.mapArea.GetComponent<Image>().color = new Color(.89f, .93f, .94f);
            popup.map = popup.mapArea.AddComponent<OpenFreeMapWebViewMap>();
            popup.map.enabled = false;
            popup.map.ShowHistoricalRoute = true;
            popup.map.ShowHistoricalTerritories = false;
            popup.routeStatus = Label(panel.transform, font, "Route status", "", .07f, .16f, .93f, .245f, 25);
            popup.shareRoute = Instantiate(buttonSource, panel.transform);
            popup.shareRoute.name = "Share route";
            popup.shareRoute.transform.localScale = Vector3.one;
            FriendsPanelUI.Place(popup.shareRoute.transform, .07f, .095f, .93f, .15f);
            popup.shareRoute.onClick = new Button.ButtonClickedEvent();
            popup.shareRoute.onClick.AddListener(() => onShareRoute());
            popup.shareRoute.gameObject.SetActive(false);
            popup.close = Instantiate(buttonSource, panel.transform);
            popup.close.name = "Close walk details";
            popup.close.transform.localScale = Vector3.one;
            FriendsPanelUI.Place(popup.close.transform, .07f, .02f, .93f, .085f);
            popup.close.onClick = new Button.ButtonClickedEvent();
            popup.close.onClick.AddListener(() => onClose());
            popup.close.interactable = true;
            popup.close.navigation = new Navigation { mode = Navigation.Mode.None };
            popup.close.GetComponentInChildren<TMP_Text>().text = "Back to walks";
            return popup;
        }

        internal void Show(SocialActivity item)
        {
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            playerName.text = item.Player.Name;
            date.text = $"Finished {item.EndedUtc.ToLocalTime():MMM d, yyyy\nh:mm tt}";
            photo.Bind(item.Player.Id, item.Player.Name, item.Player.Photo, playerName.font);
            distance.text = $"Distance    {item.DistanceMeters / 1000d:N2} km ({item.DistanceMeters:N0} m)";
            steps.text = $"Steps    {item.Steps:N0}";
            duration.text = $"Duration    {Math.Floor(item.DurationSeconds / 60):N0} min {Math.Floor(item.DurationSeconds % 60):N0} sec";
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(close.gameObject);
        }

        internal void SetRouteLoading(string message)
        {
            HideMap();
            routeStatus.text = message;
            shareRoute.interactable = false;
        }

        internal void SetRouteError(string message)
        {
            HideMap(); routeStatus.text = message;
            shareRoute.gameObject.SetActive(false);
        }

        internal void ShowRoute(string key, SocialRoute route)
        {
            HideMap();
            routeStatus.text = route.Message;
            shareRoute.gameObject.SetActive(route.IsOwner);
            shareRoute.interactable = route.IsShared || route.CanShare;
            shareRoute.GetComponentInChildren<TMP_Text>().text = route.IsShared ? "Stop sharing this route" : "Share this route with followers";
            if (route.Points.Count == 0) return;
            map.HistoricalRoutePoints = route.Points.Select(p => new Vector2(p.latitude, p.longitude)).ToList();
            map.HistoricalRouteSamples = route.Points;
            map.HistoricalRouteKey = key;
            mapArea.SetActive(true);
            map.enabled = true;
            // Opening the details popup can race a queued hide from the prior
            // walk. Request an immediate layout/state update once it is visible.
            map.ForceSync(); // Immediately redraw the selected shared route.
        }

        private void HideMap()
        {
            map.enabled = false;
            map.HistoricalRoutePoints = null; map.HistoricalRouteSamples = null;
            mapArea.SetActive(false);
        }

        internal void Close()
        {
            HideMap(); routeStatus.text = ""; shareRoute.gameObject.SetActive(false);
            gameObject.SetActive(false);
            playerName.text = date.text = distance.text = steps.text = duration.text = "";
            photo.Bind("", "", "", playerName.font);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
            previousSelection = null;
        }

        private static TMP_Text Label(Transform parent, TMP_FontAsset font, string name, string value,
            float left, float bottom, float right, float top, float size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            FriendsPanelUI.Place(text.transform, left, bottom, right, top);
            text.font = font; text.text = value; text.color = Color.white; text.richText = false;
            text.enableAutoSizing = true; text.fontSizeMin = 18; text.fontSizeMax = size;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
            return text;
        }
    }
}
