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
        internal Func<Task<ILeaderboardService>> ServiceFactory;
        internal int VisibleCardCount => cards.Count;
        internal string StatusText => status.text;

        private void Awake()
        {
            cardTemplate.gameObject.SetActive(false);
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

        public void OnRefresh() { _ = RefreshAsync(); } //refresh function for leaderboard and the discard task
        public void ShowDistance() { metric = LeaderboardMetric.Distance; OnRefresh(); }
        public void ShowSteps() { metric = LeaderboardMetric.Steps; OnRefresh(); }
        public void OnSaveNickname() { _ = SaveNicknameAsync(); }

        private async Task<ILeaderboardService> GetServiceAsync()
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
                var data = await store.LoadAsync(selected, token);
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

        internal void Render(LeaderboardSnapshot data) //display card data
        {
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
            status.text = data.Entries.Count == 0 ? "No ranked walks yet. Finish a walk and connect to the internet to join."
                : "All time · " + (data.Metric == LeaderboardMetric.Distance ? "Distance" : "Steps") + " · Swipe to see more";
            var own = data.CurrentPlayer;
            personalTotals.text = own == null ? "You · No synced walks counted yet"
                : $"You · {own.TotalDistanceMeters / 1000d:N2} km · {own.TotalSteps:N0} steps\n{own.CompletedWalkCount:N0} completed walks";
            if (own != null && !nickname.isFocused) nickname.SetTextWithoutNotify(own.DisplayName);
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
        }

        private void ClearCards()
        {
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
            CancelRequest();
            ClearCards();
            if (status != null) status.text = "";
            if (personalTotals != null) personalTotals.text = "";
            if (nickname != null) nickname.SetTextWithoutNotify("");
            foreach (var map in hiddenMaps) if (map != null) map.Resume(this);
            hiddenMaps.Clear();
        }

        private void OnDestroy()
        {
            destroyed = true;
            CancelRequest();
            service?.Dispose();
            if (refresh != null) refresh.onClick.RemoveListener(OnRefresh);
            if (distance != null) distance.onClick.RemoveListener(ShowDistance);
            if (steps != null) steps.onClick.RemoveListener(ShowSteps);
            if (saveNickname != null) saveNickname.onClick.RemoveListener(OnSaveNickname);
        }
    }
}
