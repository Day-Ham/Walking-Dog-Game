using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WalkingDog.Leaderboards
{
    public sealed class SocialPanelUI : MonoBehaviour
    {
        [SerializeField] private LeaderboardPanelUI owner;
        [SerializeField] private TMP_Text profileName, followerCount, followingCount, status, code;
        [SerializeField] private ProfilePhotoUI photo;
        [SerializeField] private Button activity, discover, following, followers, refresh, sharing, search, copy, back;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private RectTransform content;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private SocialCardUI template;
        private readonly List<GameObject> rows = new List<GameObject>();
        private readonly List<OpenFreeMapWebViewMap> maps = new List<OpenFreeMapWebViewMap>();
        private ISocialService service;
        private CancellationTokenSource pending;
        private int generation;
        private bool busy, sharesActivity, hasSnapshot;
        private string observedUser = "", target;
        private SocialTab tab = SocialTab.Activity;
        internal Func<Task<ISocialService>> ServiceFactory;
        internal string StatusText => status.text;
        internal int VisibleCardCount => rows.Count;

        private void Awake()
        {
            template.gameObject.SetActive(false);
            activity.onClick.AddListener(ShowActivity);
            discover.onClick.AddListener(ShowDiscover);
            following.onClick.AddListener(ShowFollowing);
            followers.onClick.AddListener(ShowFollowers);
            refresh.onClick.AddListener(Refresh);
            sharing.onClick.AddListener(ToggleSharing);
            search.onClick.AddListener(Search);
            copy.onClick.AddListener(CopyCode);
            back.onClick.AddListener(Back);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                if (map.enabled) { map.Suspend(this); maps.Add(map); }
            ClearIdentity();
            ShowActivity();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Back(); return; }
            if (service != null && service.AuthenticatedUserId != observedUser)
            {
                Cancel(); ClearRows(); ClearIdentity(); target = null; tab = SocialTab.Activity;
                observedUser = service.AuthenticatedUserId;
                Refresh();
            }
        }

        public void ShowActivity() => Navigate(SocialTab.Activity);
        public void ShowDiscover() => Navigate(SocialTab.Discover);
        public void ShowFollowing() => Navigate(SocialTab.Following);
        public void ShowFollowers() => Navigate(SocialTab.Followers);
        public void Refresh() { _ = RefreshAsync(); }
        public void Back()
        {
            if (tab == SocialTab.Profile) ShowActivity();
            else gameObject.SetActive(false);
        }
        public void Search()
        {
            if (!busy && !string.IsNullOrWhiteSpace(codeInput.text)) Navigate(SocialTab.Profile, codeInput.text.Trim());
        }
        private void CopyCode()
        {
            if (!busy && !string.IsNullOrEmpty(code.text)) { GUIUtility.systemCopyBuffer = code.text; status.text = "Player code copied."; }
        }
        private void Navigate(SocialTab next, string player = null)
        {
            if (busy) return;
            tab = next; target = player; Refresh();
        }
        private void ToggleSharing()
        {
            if (!busy && hasSnapshot) _ = RefreshAsync(token => service.SetActivitySharingAsync(!sharesActivity, token));
        }
        private void ChangeFollow(SocialPlayer player)
        {
            if (!busy) _ = RefreshAsync(token => service.SetFollowingAsync(player.Id, !player.Following, token));
        }

        internal async Task RefreshAsync(Func<CancellationToken, Task> mutation = null)
        {
            string mutationUser = mutation == null ? null : service?.AuthenticatedUserId;
            Cancel();
            int request = generation;
            pending = new CancellationTokenSource();
            var token = pending.Token;
            busy = true;
            ClearRows();
            status.text = mutation == null ? "Loading your circle…" : "Saving…";
            SetButtons();
            try
            {
                service = ServiceFactory != null ? await ServiceFactory() : await owner.GetServiceAsync() as ISocialService;
                if (!Current(request)) return;
                if (service == null) throw new InvalidOperationException("Social service unavailable.");
                observedUser = service.AuthenticatedUserId;
                if (string.IsNullOrEmpty(observedUser))
                { ClearIdentity(); status.text = "Sign in to follow walkers and see their activity."; return; }
                if (mutation != null)
                {
                    if (string.IsNullOrEmpty(mutationUser) || mutationUser != observedUser)
                    { ClearIdentity(); status.text = "Account changed. Tap Refresh to load your circle."; return; }
                    await mutation(token);
                }
                if (!Current(request) || service.AuthenticatedUserId != observedUser) return;
                var snapshot = await service.LoadSocialAsync(tab, target, token);
                if (!Current(request) || service.AuthenticatedUserId != observedUser) return;
                Render(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (FriendRequestException error) { if (Current(request)) status.text = error.Message; }
            catch (Exception error)
            {
                if (Current(request))
                {
                    hasSnapshot = false;
                    status.text = "Couldn't load your circle. Check your connection and tap Refresh. If you just made a change, refresh to check its result.";
                }
                Debug.LogWarning("Social request failed: " + error.GetType().Name);
            }
            finally { if (Current(request)) { busy = false; SetButtons(); } }
        }

        internal void Render(SocialSnapshot data)
        {
            ClearRows(); hasSnapshot = true; sharesActivity = data.Me.SharesActivity;
            profileName.text = data.Me.Name;
            photo.Bind(data.Me.Id, data.Me.Name, data.Me.Photo, profileName.font);
            followingCount.text = $"{data.FollowingCount:N0} following";
            followerCount.text = $"{data.FollowerCount:N0} followers";
            code.text = data.Code;
            sharing.GetComponentInChildren<TMP_Text>().text = sharesActivity ? "Walk sharing: ON" : "Walk sharing: OFF";
            if (tab == SocialTab.Profile && data.Profile != null)
            {
                // Use the resolved UID for subsequent refreshes (codes may be ambiguous with IDs).
                target = data.Profile.Id;
                AddCard(data.Profile, data.Me.Id);
                status.text = data.Profile.Id != data.Me.Id && !data.Profile.Following ? "Follow this walker to see their shared walks."
                    : data.Profile.Id != data.Me.Id && !data.Profile.SharesActivity ? "This walker has walk sharing turned off."
                    : data.Activities.Count == 0 ? "No synced walks yet." : $"{data.Profile.Name}'s latest 10 walks";
            }
            else if (tab == SocialTab.Activity)
                status.text = data.Activities.Count == 0 ? "No shared walks yet. Discover walkers to follow, or finish and sync your first walk."
                    : "Recent activity · You and people you follow · Latest 10 walks each";
            else
            {
                foreach (var player in data.Players) AddCard(player, data.Me.Id);
                status.text = tab == SocialTab.Discover ? "Discover · Up to 50 ranked walkers. Find anyone else by player code."
                    : data.Players.Count == 0 ? tab == SocialTab.Followers ? "No followers yet. Copy your code and share it." : "You aren't following anyone yet. Try Discover."
                    : tab == SocialTab.Followers ? "Your followers · Follow back to see their shared activity." : "Following · Tap View walks to open a profile.";
            }
            foreach (var item in data.Activities) AddCard(item.Player, data.Me.Id, item);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
        }

        private void AddCard(SocialPlayer player, string viewer, SocialActivity item = null)
        {
            var card = Instantiate(template, content);
            card.name = item == null ? "Social player " + player.Id : "Activity " + player.Id + " " + item.Id;
            card.gameObject.SetActive(true);
            card.Bind(player, viewer, item, ChangeFollow, p => Navigate(SocialTab.Profile, p.Id));
            rows.Add(card.gameObject);
        }
        private void SetButtons()
        {
            bool signedIn = service != null && !string.IsNullOrEmpty(service.AuthenticatedUserId);
            activity.interactable = !busy && tab != SocialTab.Activity;
            discover.interactable = !busy && tab != SocialTab.Discover;
            following.interactable = !busy && tab != SocialTab.Following;
            followers.interactable = !busy && tab != SocialTab.Followers;
            refresh.interactable = !busy;
            search.interactable = codeInput.interactable = !busy && signedIn;
            sharing.interactable = copy.interactable = !busy && signedIn && hasSnapshot;
        }
        private bool Current(int request) => this != null && isActiveAndEnabled && request == generation;
        private void Cancel() { generation++; pending?.Cancel(); pending?.Dispose(); pending = null; busy = false; }
        private void ClearIdentity()
        {
            hasSnapshot = false; sharesActivity = false; profileName.text = "Your circle";
            followerCount.text = followingCount.text = code.text = ""; codeInput.SetTextWithoutNotify("");
            sharing.GetComponentInChildren<TMP_Text>().text = "Walk sharing: OFF";
            photo.Bind("", "You", "", profileName.font);
        }
        private void ClearRows()
        {
            foreach (var row in rows) if (row != null)
            { row.SetActive(false); if (Application.isPlaying) Destroy(row); else DestroyImmediate(row); }
            rows.Clear();
        }
        private void OnDisable()
        {
            Cancel(); ClearRows(); ClearIdentity();
            foreach (var map in maps) if (map != null) map.Resume(this);
            maps.Clear();
        }
    }
}
