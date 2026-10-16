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
    // Attached to the existing Leaderboard Screen. Its existing SetActive buttons
    // remain usable: enabling loads the board; disabling cancels and restores maps.
    public sealed class LeaderboardPanelUI : MonoBehaviour
    {
        [SerializeField] private RectTransform content; // the content and position of the template
        [SerializeField] private LeaderboardCardUI cardTemplate;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Text personalTotals;
        [SerializeField] private Button refresh;
        [SerializeField] private Button distance;
        [SerializeField] private Button steps;

        private readonly List<GameObject> cards = new List<GameObject>();
        private readonly List<OpenFreeMapWebViewMap> hiddenMaps = new List<OpenFreeMapWebViewMap>();
        private ILeaderboardService service;
        private Task<ILeaderboardService> initialization;
        private CancellationTokenSource pending;
        private int generation;
        private bool destroyed;
        private bool busy;
        private string observedUser = "";
        private LeaderboardMetric metric = LeaderboardMetric.Distance; //display the leaderboard distance
        private LeaderboardScope scope = LeaderboardScope.Global;
        // Assign the existing scene controls in the Inspector. These are never created at runtime.
        [SerializeField] private Button globalTab, friendsTab, manageFriends;
        [SerializeField] private FriendsPanelUI friendsPanel;
        [SerializeField] private Button uploadPhoto, googlePhoto;
        [SerializeField] private ProfilePhotoUI ownPhoto;
        internal Func<Task<ILeaderboardService>> ServiceFactory;
        internal int VisibleCardCount => cards.Count;
        internal string StatusText => status.text;

        // Caches UI elements, hides card template, and registers button click listeners.
        private void Awake()
        {
            // DELETE this call after Global, Friends, and Manage Friends are scene objects.
           // EnsureFriendControls();
            cardTemplate.gameObject.SetActive(false);

            // DELETE these listeners after assigning the same public methods in each Button's
            // Inspector On Click() event. Keep the public methods themselves.
            refresh.onClick.AddListener(OnRefresh);
            distance.onClick.AddListener(ShowDistance);
            steps.onClick.AddListener(ShowSteps);
            if (uploadPhoto != null) uploadPhoto.onClick.AddListener(UploadProfilePhoto);
            else Debug.LogWarning("Assign Upload Photo on LeaderboardPanelUI in the Inspector.");
            if (googlePhoto != null) googlePhoto.onClick.AddListener(UseGooglePhoto);
            else Debug.LogWarning("Assign Google Photo on LeaderboardPanelUI in the Inspector.");
            if (ownPhoto == null) Debug.LogWarning("Assign Own Photo on LeaderboardPanelUI in the Inspector.");
        }

        // Suspends active map webviews and triggers an initial leaderboard refresh when displayed.
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                if (map.enabled) { map.Suspend(this); if (!hiddenMaps.Contains(map)) hiddenMaps.Add(map); }
            OnRefresh();
        }

        // Handles escape key back navigation and detects auth user changes to reload rankings.
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { gameObject.SetActive(false); return; }
            if (service != null && observedUser != service.AuthenticatedUserId)
            {
                observedUser = service.AuthenticatedUserId;
                ClearCards();
                personalTotals.text = "";
                OnRefresh();
            }
        }

        #region KEEP — CONNECT THESE METHODS TO INSPECTOR BUTTON EVENTS

        // Refreshes the leaderboard view asynchronously.
        public void OnRefresh() { _ = RefreshAsync(); }

        // Switches ranking metric to distance and refreshes the leaderboard.
        public void ShowDistance() { metric = LeaderboardMetric.Distance; OnRefresh(); }

        // Switches ranking metric to steps and refreshes the leaderboard.
        public void ShowSteps() { metric = LeaderboardMetric.Steps; OnRefresh(); }

        // Switches leaderboard scope to global rankings and refreshes.
        public void ShowGlobal() { scope = LeaderboardScope.Global; OnRefresh(); }

        // Switches leaderboard scope to friends rankings and refreshes.
        public void ShowFriends() { scope = LeaderboardScope.Friends; OnRefresh(); }

        // Initiates selecting and uploading a custom profile photo.
        public void UploadProfilePhoto() { _ = ChangePhotoAsync(true); }

        // Clears any custom profile photo and reverts to the Google account photo.
        public void UseGooglePhoto() { _ = ChangePhotoAsync(false); }

        // Opens the friends management modal overlay.
        public void OpenFriendsPanel()
        {
            if (friendsPanel == null)
            {
                Debug.LogWarning("Assign Friends Panel on LeaderboardPanelUI in the Inspector.");
                return;
            }
            friendsPanel.Initialize(this);
            friendsPanel.transform.SetAsLastSibling();
            friendsPanel.gameObject.SetActive(true);
        }

        #endregion

       

        // Retrieves or asynchronously initializes the leaderboard service instance.
        internal async Task<ILeaderboardService> GetServiceAsync()
        {
            if (service != null) return service;
            if (initialization == null) initialization = ServiceFactory != null ? ServiceFactory() : CreateServiceAsync();
            var created = await initialization;
            if (destroyed) { created.Dispose(); throw new OperationCanceledException(); }
            service = created;
            observedUser = service.AuthenticatedUserId;
            return service;
        }

        // Creates a concrete Firebase leaderboard service using the Firebase walk bootstrap.
        private static async Task<ILeaderboardService> CreateServiceAsync()
        {
            var manager = StepCountAndGpsManager.Instance;
            if (manager == null) throw new InvalidOperationException("Walk manager is unavailable.");
            return await FirebaseLeaderboardService.CreateAsync(manager.GetComponent<FirebaseWalkBootstrap>());
        }

        // Prepares a new cancellation token source and updates status text for an outgoing request.
        private int BeginRequest(string message)
        {
            CancelRequest();
            pending = new CancellationTokenSource();
            busy = true;
            status.text = message;
            SetButtons();
            return generation;
        }

        // Checks whether a request matches the active generation and the UI is still mounted.
        private bool IsCurrent(int request) => !destroyed && isActiveAndEnabled && request == generation;

        // Loads the latest rankings from the leaderboard service and renders the card list.
        public async Task RefreshAsync()
        {
            if (!isActiveAndEnabled || destroyed) return;
            var selected = metric;
            var selectedScope = scope;
            int request = BeginRequest("Loading leaderboard…");
            var token = pending.Token;
            ClearCards();
            personalTotals.text = "";
            try
            {
                var store = await GetServiceAsync();
                if (!IsCurrent(request)) return;
                if (string.IsNullOrEmpty(store.AuthenticatedUserId))
                { status.text = "Sign in to see the leaderboard."; return; }
                var data = await store.LoadAsync(selected, selectedScope, token);
                if (!IsCurrent(request)) return;
                Render(data);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!IsCurrent(request)) return;
                // Failed initialization can be retried; never convert failures to zero totals.
                if (service == null) initialization = null;
                status.text = "Couldn't load rankings. Check your connection and tap Refresh.";
                Debug.LogWarning("Leaderboard load failed: " + exception.GetType().Name);
            }
            finally { if (IsCurrent(request)) { busy = false; SetButtons(); } }
        }

        // Instantiates and binds card UI instances for all entries in the snapshot.
        internal void Render(LeaderboardSnapshot data)
        {
            ClearCards();
            for (int i = 0; i < data.Entries.Count; i++)
            {
                var card = Instantiate(cardTemplate, content);
                card.name = "Player " + (i + 1);
                card.Bind(data.Entries[i], i + 1, data.Entries[i].PlayerId == data.CurrentPlayerId, data.Metric);
                card.gameObject.SetActive(true);
                cards.Add(card.gameObject);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content); 
            scroll.StopMovement();
            scroll.horizontalNormalizedPosition = 0;
            status.text = data.Entries.Count == 0
                ? data.Scope == LeaderboardScope.Friends ? "No ranked walks yet. Add friends and sync a completed walk."
                    : "No activity synced yet. Take some steps and connect to the internet to join."
                : (data.Scope == LeaderboardScope.Friends ? "Friends" : "Global") + " · All time · "
                    + (data.Metric == LeaderboardMetric.Distance ? "Distance" : "Steps") + " · Swipe to see more";
            var own = data.CurrentPlayer;
            if (ownPhoto != null)
            {
                ownPhoto.gameObject.SetActive(true);
                ownPhoto.Bind(data.CurrentPlayerId, own?.DisplayName ?? "You", data.CurrentPlayerPhotoUrl, status.font);
            }
            personalTotals.text = own == null ? "You · No synced walks counted yet"
                : $"You · {own.TotalDistanceMeters / 1000d:N2} km · {own.TotalSteps:N0} steps\n{own.CompletedWalkCount:N0} completed walks";
        }

        // Handles photo picking or reverting and updates the player's profile photo in the service.
        private async Task ChangePhotoAsync(bool upload)
        {
            if (busy || destroyed || !isActiveAndEnabled) return;
            int request = BeginRequest(upload ? "Choose a profile photo…" : "Restoring Google photo…");
            var token = pending.Token;
            try
            {
                var store = await GetServiceAsync();
                if (!IsCurrent(request)) return;
                var uid = store.AuthenticatedUserId;
                if (!(store is IPlayerProfileService profiles)) throw new InvalidOperationException("Profile photos are unavailable.");
                var photo = upload ? await AndroidProfilePhotoPicker.PickAsync(token) : "";
                if (!IsCurrent(request) || uid != store.AuthenticatedUserId) return;
                if (photo == null) { status.text = "Photo selection cancelled."; return; }
                status.text = "Saving profile photo…";
                await profiles.SaveProfilePhotoAsync(photo, token);
                if (!IsCurrent(request)) return;
                await RefreshAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (IsCurrent(request)) status.text = "Couldn't change photo. Refresh to check, then try again.";
#if !UNITY_ANDROID || UNITY_EDITOR
                if (IsCurrent(request) && upload) status.text = "Choose a phone photo in the Android app.";
#endif
                Debug.LogWarning("Profile photo change failed: " + exception.GetType().Name);
            }
            finally { if (IsCurrent(request)) { busy = false; SetButtons(); } }
        }

        // Updates the interactable status of metric switches, scope tabs, and action buttons.
        private void SetButtons()
        {
            refresh.interactable = !busy;
            distance.interactable = metric != LeaderboardMetric.Distance;
            steps.interactable = metric != LeaderboardMetric.Steps;
            bool signedIn = !busy && service != null && !string.IsNullOrEmpty(service.AuthenticatedUserId);
            if (uploadPhoto != null) uploadPhoto.interactable = signedIn;
            if (googlePhoto != null) googlePhoto.interactable = signedIn;
            if (globalTab != null) globalTab.interactable = scope != LeaderboardScope.Global;
            if (friendsTab != null) friendsTab.interactable = scope != LeaderboardScope.Friends;
        }

        // Destroys all currently instantiated leaderboard card GameObjects.
        private void ClearCards()
        {
            if (ownPhoto != null) ownPhoto.gameObject.SetActive(false);
            foreach (var card in cards)
            {
                if (card == null) continue;
                card.SetActive(false);
                if (Application.isPlaying) Destroy(card); else DestroyImmediate(card);
            }
            cards.Clear();
        }

        // Cancels pending network operations and resets busy state.
        private void CancelRequest()
        {
            generation++;
            pending?.Cancel();
            pending?.Dispose();
            pending = null;
            busy = false;
        }

        // Cleans up active requests, clears UI cards, and resumes suspended map webviews.
        private void OnDisable()
        {
            if (friendsPanel != null) friendsPanel.gameObject.SetActive(false);
            CancelRequest();
            ClearCards();
            if (status != null) status.text = "";
            if (personalTotals != null) personalTotals.text = "";
            if (ownPhoto != null) ownPhoto.gameObject.SetActive(false);
            foreach (var map in hiddenMaps) if (map != null) map.Resume(this);
            hiddenMaps.Clear();
        }

        // Disposes service resources and unbinds registered button listeners upon destruction.
        private void OnDestroy()
        {
            destroyed = true;
            CancelRequest();
            service?.Dispose();
            // DELETE these RemoveListener calls only after the matching Awake AddListener
            // calls have been deleted and events are wired through the Inspector instead.
            if (refresh != null) refresh.onClick.RemoveListener(OnRefresh);
            if (distance != null) distance.onClick.RemoveListener(ShowDistance);
            if (steps != null) steps.onClick.RemoveListener(ShowSteps);
            if (uploadPhoto != null) uploadPhoto.onClick.RemoveListener(UploadProfilePhoto);
            if (googlePhoto != null) googlePhoto.onClick.RemoveListener(UseGooglePhoto);
        }
    }
}
