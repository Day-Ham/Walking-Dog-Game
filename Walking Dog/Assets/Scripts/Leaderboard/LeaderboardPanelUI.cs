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
        [SerializeField] private TMP_InputField nickname;
        [SerializeField] private Button saveNickname;

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
        // KEEP: assign these once the runtime-created controls are added to the scene.
        [SerializeField] private Button globalTab, friendsTab, manageFriends;
        [SerializeField] private FriendsPanelUI friendsPanel;
        [SerializeField] private Button uploadPhoto, googlePhoto;
        [SerializeField] private ProfilePhotoUI ownPhoto;
        internal Func<Task<ILeaderboardService>> ServiceFactory;
        internal int VisibleCardCount => cards.Count;
        internal string StatusText => status.text;

        private void Awake()
        {
            // DELETE this call after Global, Friends, and Manage Friends are scene objects.
           // EnsureFriendControls();
            cardTemplate.gameObject.SetActive(false);
            EnsureProfileControls();

            // DELETE these listeners after assigning the same public methods in each Button's
            // Inspector On Click() event. Keep the public methods themselves.
            refresh.onClick.AddListener(OnRefresh);
            distance.onClick.AddListener(ShowDistance);
            steps.onClick.AddListener(ShowSteps);
            saveNickname.onClick.AddListener(OnSaveNickname);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                if (map.enabled) { map.Suspend(this); if (!hiddenMaps.Contains(map)) hiddenMaps.Add(map); }
            OnRefresh();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { gameObject.SetActive(false); return; }
            if (service != null && observedUser != service.AuthenticatedUserId)
            {
                observedUser = service.AuthenticatedUserId;
                nickname.text = "";
                ClearCards();
                personalTotals.text = "";
                OnRefresh();
            }
        }

        #region KEEP — CONNECT THESE METHODS TO INSPECTOR BUTTON EVENTS

        // Refresh button -> OnRefresh
        public void OnRefresh() { _ = RefreshAsync(); }
        // Distance button -> ShowDistance
        public void ShowDistance() { metric = LeaderboardMetric.Distance; OnRefresh(); }
        // Steps button -> ShowSteps
        public void ShowSteps() { metric = LeaderboardMetric.Steps; OnRefresh(); }
        // Global tab -> ShowGlobal
        public void ShowGlobal() { scope = LeaderboardScope.Global; OnRefresh(); }
        // Friends tab -> ShowFriends
        public void ShowFriends() { scope = LeaderboardScope.Friends; OnRefresh(); }
        // Save nickname button -> OnSaveNickname
        public void OnSaveNickname() { _ = SaveNicknameAsync(); }
        public void UploadProfilePhoto() { _ = ChangePhotoAsync(true); }
        public void UseGooglePhoto() { _ = ChangePhotoAsync(false); }
        // Manage friends button -> OpenFriendsPanel
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

        private static async Task<ILeaderboardService> CreateServiceAsync()
        {
            var manager = StepCountAndGpsManager.Instance;
            if (manager == null) throw new InvalidOperationException("Walk manager is unavailable.");
            return await FirebaseLeaderboardService.CreateAsync(manager.GetComponent<FirebaseWalkBootstrap>());
        }

        private int BeginRequest(string message)
        {
            CancelRequest();
            pending = new CancellationTokenSource();
            busy = true;
            status.text = message;
            SetButtons();
            return generation;
        }

        private bool IsCurrent(int request) => !destroyed && isActiveAndEnabled && request == generation;

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

       
    
        internal void Render(LeaderboardSnapshot data)
        {
            EnsureProfileControls();
            ClearCards(); //clear previous garbage card before rendering new data
            for (int i = 0; i < data.Entries.Count; i++) // check all entries
            {
                var card = Instantiate(cardTemplate, content); // create the card game object
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
                    : "No ranked walks yet. Finish a walk and connect to the internet to join."
                : (data.Scope == LeaderboardScope.Friends ? "Friends" : "Global") + " · All time · "
                    + (data.Metric == LeaderboardMetric.Distance ? "Distance" : "Steps") + " · Swipe to see more";
            var own = data.CurrentPlayer;
            ownPhoto.gameObject.SetActive(true);
            ownPhoto.Bind(data.CurrentPlayerId, own?.DisplayName ?? "You", data.CurrentPlayerPhotoUrl, status.font);
            personalTotals.text = own == null ? "You · No synced walks counted yet"
                : $"You · {own.TotalDistanceMeters / 1000d:N2} km · {own.TotalSteps:N0} steps\n{own.CompletedWalkCount:N0} completed walks";
            if (own != null && !nickname.isFocused) nickname.SetTextWithoutNotify(own.DisplayName);
        }

        private void EnsureProfileControls()
        {
            var parent = refresh.transform.parent;
            if (uploadPhoto == null) uploadPhoto = FriendsPanelUI.MakeButton(parent, refresh, "Upload profile photo", "Phone photo", .10f, .154f, .48f, .197f);
            if (googlePhoto == null) googlePhoto = FriendsPanelUI.MakeButton(parent, refresh, "Use Google photo", "Use Google photo", .52f, .154f, .90f, .197f);
            uploadPhoto.onClick.RemoveListener(UploadProfilePhoto);
            googlePhoto.onClick.RemoveListener(UseGooglePhoto);
            uploadPhoto.onClick.AddListener(UploadProfilePhoto);
            googlePhoto.onClick.AddListener(UseGooglePhoto);
            if (ownPhoto != null) return;
            FriendsPanelUI.Place(personalTotals.transform, .21f, .20f, .90f, .24f);
            var photo = new GameObject("Your profile photo", typeof(RectTransform), typeof(Image), typeof(ProfilePhotoUI));
            photo.transform.SetParent(parent, false);
            FriendsPanelUI.Place(photo.transform, .10f, .20f, .19f, .24f);
            ownPhoto = photo.GetComponent<ProfilePhotoUI>();
            ownPhoto.gameObject.SetActive(false);
            if (friendsPanel != null) friendsPanel.transform.SetAsLastSibling();
        }

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

        private async Task SaveNicknameAsync() // save nickname function for the leaderboard
        {
            if (busy || destroyed || !isActiveAndEnabled) return;
            var name = nickname.text.Trim();
            if (!LeaderboardNames.IsValid(name))
            { status.text = "Use 3–24 letters, numbers, spaces, _ or -. Start with a letter or number."; return; }
            int request = BeginRequest("Saving nickname…");
            var token = pending.Token;
            try
            {
                var store = await GetServiceAsync();
                if (!IsCurrent(request)) return;
                await store.SaveDisplayNameAsync(name, token);
                if (IsCurrent(request)) status.text = "Nickname saved. Tap Refresh to see your updated card.";
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (IsCurrent(request)) status.text = "Couldn't save nickname. Check your connection and try again.";
                Debug.LogWarning("Leaderboard nickname save failed: " + exception.GetType().Name);
            }
            finally { if (IsCurrent(request)) { busy = false; SetButtons(); } }
        }

        private void SetButtons() // set buttons 
        {
            refresh.interactable = !busy;
            distance.interactable = metric != LeaderboardMetric.Distance;
            steps.interactable = metric != LeaderboardMetric.Steps;
            saveNickname.interactable = !busy && service != null && !string.IsNullOrEmpty(service.AuthenticatedUserId);
            nickname.interactable = saveNickname.interactable;
            if (uploadPhoto != null) uploadPhoto.interactable = googlePhoto.interactable = saveNickname.interactable;
            if (globalTab != null) globalTab.interactable = scope != LeaderboardScope.Global;
            if (friendsTab != null) friendsTab.interactable = scope != LeaderboardScope.Friends;
        }

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

        private void CancelRequest()
        {
            generation++;
            pending?.Cancel();
            pending?.Dispose();
            pending = null;
            busy = false;
        }

        private void OnDisable()
        {
            if (friendsPanel != null) friendsPanel.gameObject.SetActive(false);
            CancelRequest();
            ClearCards();
            if (status != null) status.text = "";
            if (personalTotals != null) personalTotals.text = "";
            if (nickname != null) nickname.SetTextWithoutNotify("");
            if (ownPhoto != null) ownPhoto.gameObject.SetActive(false);
            foreach (var map in hiddenMaps) if (map != null) map.Resume(this);
            hiddenMaps.Clear();
        }




        /* internal void EnsureFriendControls()
        {
            if (globalTab != null) return;
            var parent = refresh.transform.parent;
            globalTab = FriendsPanelUI.MakeButton(parent, refresh, "Global rankings", "Global", .10f, .695f, .48f, .745f);
            friendsTab = FriendsPanelUI.MakeButton(parent, refresh, "Friends rankings", "Friends", .52f, .695f, .90f, .745f);
            manageFriends = FriendsPanelUI.MakeButton(parent, refresh, "Manage friends", "Manage friends", .53f, .025f, .90f, .077f);
            FriendsPanelUI.Place(scroll.transform, .10f, .30f, .90f, .68f);
            globalTab.onClick.AddListener(ShowGlobal);
            friendsTab.onClick.AddListener(ShowFriends);
            manageFriends.onClick.AddListener(OpenFriendsPanel);
        }

        #endregion
       */
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
            if (saveNickname != null) saveNickname.onClick.RemoveListener(OnSaveNickname);
        }
    }
}
