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
        [SerializeField] private TMP_InputField nickname;
        [SerializeField] private Button saveNickname;
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
        private SocialWalkDetailsUI walkDetails;
        private CanvasGroup backgroundControls;
        private bool backgroundWasInteractable;
        private CancellationTokenSource routePending;
        private int routeGeneration;
        private SocialActivity selectedWalk;
        private SocialRoute selectedRoute;
        internal Func<Task<ISocialService>> ServiceFactory;
        internal string StatusText => status.text;
        internal int VisibleCardCount => rows.Count;

        // Deactivates template card and wires all button event listeners.
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
            if (saveNickname != null) saveNickname.onClick.AddListener(OnSaveNickname);
            else Debug.LogWarning("Assign Save Nickname on SocialPanelUI in the Inspector.");
        }

        // Suspends active map webviews, clears identity state, and displays the activity feed.
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                if (map.enabled) { map.Suspend(this); maps.Add(map); }
            ClearIdentity();
            ShowActivity();
        }

        // Handles escape key back navigation and refreshes data when the authenticated user changes.
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

        // Navigates to the recent activity feed tab.
        public void ShowActivity() => Navigate(SocialTab.Activity);

        // Navigates to the discover walkers tab.
        public void ShowDiscover() => Navigate(SocialTab.Discover);

        // Navigates to the list of followed players tab.
        public void ShowFollowing() => Navigate(SocialTab.Following);

        // Navigates to the list of followers tab.
        public void ShowFollowers() => Navigate(SocialTab.Followers);

        // Asynchronously refreshes the social snapshot.
        public void Refresh() { _ = RefreshAsync(); }

        // Handles back navigation, closing walk details if open or exiting the social panel.
        public void Back()
        {
            if (walkDetails != null && walkDetails.gameObject.activeSelf) { CloseWalkDetails(); return; }
            if (tab == SocialTab.Profile) ShowActivity();
            else gameObject.SetActive(false);
        }

        // Searches for a player profile using the entered player code.
        public void Search()
        {
            if (!busy && !string.IsNullOrWhiteSpace(codeInput.text)) Navigate(SocialTab.Profile, codeInput.text.Trim());
        }

        // Copies the current player's friend/social code to the system clipboard.
        private void CopyCode()
        {
            if (!busy && !string.IsNullOrEmpty(code.text)) { GUIUtility.systemCopyBuffer = code.text; status.text = "Player code copied."; }
        }

        // Switches the current social tab and optional target player ID, then refreshes.
        private void Navigate(SocialTab next, string player = null)
        {
            if (busy) return;
            tab = next; target = player; Refresh();
        }

        // Toggles activity sharing on or off for the authenticated player.
        private void ToggleSharing()
        {
            if (!busy && hasSnapshot) _ = RefreshAsync(token => service.SetActivitySharingAsync(!sharesActivity, token));
        }

        // Follows or unfollows the specified player.
        private void ChangeFollow(SocialPlayer player)
        {
            if (!busy) _ = RefreshAsync(token => service.SetFollowingAsync(player.Id, !player.Following, token));
        }

        // Dispatches saving the entered nickname to the leaderboard service.
        public void OnSaveNickname() { _ = SaveNicknameAsync(); }

        // Validates and saves the new player display name to Firestore.
        private async Task SaveNicknameAsync()
        {
            if (busy || nickname == null) return;
            var name = nickname.text.Trim();
            if (!LeaderboardNames.IsValid(name))
            { status.text = "Use 3–24 letters, numbers, spaces, _ or -. Start with a letter or number."; return; }
            Cancel();
            int request = generation;
            pending = new CancellationTokenSource();
            var token = pending.Token;
            busy = true;
            status.text = "Saving nickname…";
            SetButtons();
            try
            {
                ILeaderboardService svc = (service as ILeaderboardService)
                    ?? (ServiceFactory != null ? (await ServiceFactory()) as ILeaderboardService : null)
                    ?? (owner != null ? await owner.GetServiceAsync() : null);
                if (!Current(request)) return;
                if (svc == null) throw new InvalidOperationException("Leaderboard service unavailable.");
                await svc.SaveDisplayNameAsync(name, token);
                if (Current(request))
                {
                    profileName.text = name;
                    status.text = "Nickname saved.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (Current(request)) status.text = "Couldn't save nickname. Check your connection and try again.";
                Debug.LogWarning("Profile nickname save failed: " + exception.GetType().Name);
            }
            finally { if (Current(request)) { busy = false; SetButtons(); } }
        }

        // Loads social snapshot data, optionally executing a mutation task like follow or privacy toggle first.
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

        // Renders the profile header and instantiates card rows for players and recent activities.
        internal void Render(SocialSnapshot data)
        {
            ClearRows(); hasSnapshot = true; sharesActivity = data.Me.SharesActivity;
            profileName.text = data.Me.Name;
            photo.Bind(data.Me.Id, data.Me.Name, data.Me.Photo, profileName.font);
            followingCount.text = $"{data.FollowingCount:N0} following";
            followerCount.text = $"{data.FollowerCount:N0} followers";
            code.text = data.Code;
            sharing.GetComponentInChildren<TMP_Text>().text = sharesActivity ? "Walk sharing: ON" : "Walk sharing: OFF";
            if (nickname != null && !nickname.isFocused) nickname.SetTextWithoutNotify(data.Me.Name);
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

        // Instantiates and binds a social card for a player or walk activity.
        private void AddCard(SocialPlayer player, string viewer, SocialActivity item = null)
        {
            var card = Instantiate(template, content);
            card.name = item == null ? "Social player " + player.Id : "Activity " + player.Id + " " + item.Id;
            card.gameObject.SetActive(true);
            card.Bind(player, viewer, item, ChangeFollow, p => Navigate(SocialTab.Profile, p.Id),
                ShowWalkDetails, tab == SocialTab.Profile && target == player.Id);
            rows.Add(card.gameObject);
        }

        // Opens the walk details popup and starts fetching GPS route coordinates.
        private void ShowWalkDetails(SocialActivity item)
        {
            if (busy || item == null || (service != null && service.AuthenticatedUserId != observedUser)) return;
            if (walkDetails == null)
                walkDetails = SocialWalkDetailsUI.Create(transform, refresh, profileName.font, CloseWalkDetails, ToggleRouteSharing);
            if (!walkDetails.gameObject.activeSelf)
            {
                // The popup is a sibling of the safe-area controls, so keyboard
                // navigation cannot activate the list or tabs underneath it.
                backgroundControls = back.transform.parent.GetComponent<CanvasGroup>();
                if (backgroundControls == null) backgroundControls = back.transform.parent.gameObject.AddComponent<CanvasGroup>();
                backgroundWasInteractable = backgroundControls.interactable;
                backgroundControls.interactable = false;
            }
            walkDetails.Show(item);
            selectedWalk = item;
            selectedRoute = null;
            _ = LoadWalkRouteAsync(item);
        }

        // Toggles sharing of the selected walk route between public and private.
        private void ToggleRouteSharing()
        {
            if (selectedWalk != null && selectedRoute != null && selectedRoute.IsOwner)
                _ = LoadWalkRouteAsync(selectedWalk, !selectedRoute.IsShared);
        }

        // Asynchronously loads route points for a walk activity from local device storage or Firestore.
        private async Task LoadWalkRouteAsync(SocialActivity item, bool? share = null)
        {
            routePending?.Cancel(); routePending?.Dispose();
            routePending = new CancellationTokenSource();
            var token = routePending.Token;
            int request = ++routeGeneration;
            string viewer = observedUser;
            walkDetails.SetRouteLoading(share == null ? "Loading this walk's route…" : "Updating route sharing…");
            try
            {
                if (!(service is ISocialRouteService routes))
                { walkDetails.SetRouteError("Route loading is unavailable."); return; }
                if (service.AuthenticatedUserId != viewer) return;
                if (share != null) await routes.SetRouteSharedAsync(item.Player.Id, item.Id, share.Value, token);
                if (!RouteCurrent(request, viewer)) return;
                var route = await routes.LoadRouteAsync(item.Player.Id, item.Id, token);
                if (!RouteCurrent(request, viewer)) return;
                selectedRoute = route;
                walkDetails.ShowRoute(item.Player.Id + ":" + item.Id, route);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (RouteCurrent(request, viewer))
                {
                    var firebase = error as Firebase.FirebaseException;
                    Debug.LogWarning("Social route request failed: " + error.GetType().Name
                        + (firebase == null ? "" : " (code " + firebase.ErrorCode + ")"));
                    bool denied = firebase != null && firebase.ErrorCode == (int)Firebase.Firestore.FirestoreError.PermissionDenied;
                    walkDetails.SetRouteError(denied
                        ? "Route access was denied. The walker must share this route and keep Walk sharing ON."
                        : "Couldn't load the route. Check your connection, then close and reopen this walk to retry.");
                }
            }
        }

        // Checks whether a route request generation matches the currently displayed popup.
        private bool RouteCurrent(int request, string viewer) => this != null && isActiveAndEnabled
            && request == routeGeneration && walkDetails != null && walkDetails.gameObject.activeSelf
            && service != null && service.AuthenticatedUserId == viewer;

        // Dismisses the walk details popup and re-enables background UI interactions.
        private void CloseWalkDetails()
        {
            routeGeneration++; routePending?.Cancel(); routePending?.Dispose(); routePending = null;
            selectedWalk = null; selectedRoute = null;
            if (walkDetails == null || !walkDetails.gameObject.activeSelf) return;
            if (backgroundControls != null) backgroundControls.interactable = backgroundWasInteractable;
            walkDetails.Close();
        }

        // Updates button interactability based on active tab and authentication state.
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
            if (nickname != null) nickname.interactable = !busy && signedIn;
            if (saveNickname != null) saveNickname.interactable = !busy && signedIn;
        }

        // Verifies whether an asynchronous operation corresponds to the current UI generation.
        private bool Current(int request) => this != null && isActiveAndEnabled && request == generation;

        // Cancels any in-flight asynchronous operations.
        private void Cancel() { generation++; pending?.Cancel(); pending?.Dispose(); pending = null; busy = false; }

        // Resets user identity labels and photo display to defaults.
        private void ClearIdentity()
        {
            hasSnapshot = false; sharesActivity = false; profileName.text = "Your circle";
            followerCount.text = followingCount.text = code.text = ""; codeInput.SetTextWithoutNotify("");
            sharing.GetComponentInChildren<TMP_Text>().text = "Walk sharing: OFF";
            if (nickname != null) nickname.SetTextWithoutNotify("");
            photo.Bind("", "You", "", profileName.font);
        }

        // Closes popup and clears instantiated card GameObjects.
        private void ClearRows()
        {
            CloseWalkDetails();
            foreach (var row in rows) if (row != null)
            { row.SetActive(false); if (Application.isPlaying) Destroy(row); else DestroyImmediate(row); }
            rows.Clear();
        }

        // Cancels operations, clears instantiated rows, and resumes suspended map webviews.
        private void OnDisable()
        {
            Cancel(); ClearRows(); ClearIdentity();
            foreach (var map in maps) if (map != null) map.Resume(this);
            maps.Clear();
        }
    }
}
