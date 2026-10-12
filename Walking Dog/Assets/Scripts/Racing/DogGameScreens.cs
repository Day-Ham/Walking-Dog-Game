using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WalkingDog.Racing
{
    public sealed class DogGameScreens : MonoBehaviour
    {
        [Serializable] public sealed class StatRow
        {
            public DogStat stat;
            public TMP_Text value;
            public Slider bar;
            public Button train;
        }
        [Serializable] public sealed class RaceLane
        {
            public RectTransform marker;
            public TMP_Text details;
            public Image iconImage;
        }
        [Serializable] private sealed class PendingTraining { public string id; public DogStat stat; }

        [SerializeField] private GameObject trainingScreen, raceScreen, lobbyScreen;
        [SerializeField] private GameObject lobbyFindSection, lobbyRoomSection;
        [SerializeField] private TMP_Text trainingStatus, points, dogSummary, raceStatus, raceSummary, results;
        [SerializeField] private TMP_Text lobbyStatus, lobbyRoomCodeText;
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private Button startRace, refreshTraining;
        [SerializeField] private Button createRoomButton, joinRoomButton, practiceRaceButton;
        [SerializeField] private Button startLobbyRaceButton, leaveRoomButton;
        [SerializeField] private TMP_Text[] lobbyEntrantRows;
        [SerializeField] private StatRow[] stats;
        [SerializeField] private RaceLane[] lanes;
        private readonly List<OpenFreeMapWebViewMap> maps = new List<OpenFreeMapWebViewMap>();
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private IDogTrainingStore store;
        private IRaceLobbyService lobbyService;
        private RaceRoom currentRoom;
        private IDisposable roomSubscription;
        private PendingTraining pendingTraining;
        private string owner = "";
        private bool busy, initialized;
        private int generation, raceNumber;
        private DogRaceSimulation race;
        private DogStats snapshot;
        internal Func<string> OwnerReader;
        internal Func<Task<IDogTrainingStore>> StoreFactory;
        internal Func<Task<IRaceLobbyService>> LobbyServiceFactory;
        internal DogStats Snapshot => snapshot?.Copy();
        internal DogRaceSimulation Race => race;
        internal RaceRoom CurrentRoom => currentRoom;
        internal string TrainingStatus => trainingStatus != null ? trainingStatus.text : "";
        internal string LobbyStatus => lobbyStatus != null ? lobbyStatus.text : "";
        internal bool IsBusy => busy;
        private FirebaseWalkBootstrap Bootstrap => StepCountAndGpsManager.Instance?.GetComponent<FirebaseWalkBootstrap>();
        private string CurrentOwner => OwnerReader != null ? OwnerReader() : Bootstrap?.Wallet?.Owner ?? "";
        private string CurrentPlayerName => FirebaseAuth.DefaultInstance?.CurrentUser?.DisplayName is string name && !string.IsNullOrWhiteSpace(name) ? name : "Your Dog";
        private string CurrentEquippedItemId => PlayerPrefs.GetString(CollarInventoryStore.EquippedItemIdKey, "");

        private void Update()
        {
            if (!Visible) return;
            if (initialized && owner != CurrentOwner) { ResetAccount(); _ = RefreshAsync(); }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (raceScreen.activeSelf && race != null && !race.Completed)
            { race.Advance(Math.Min(.25, Time.unscaledDeltaTime)); RenderRace(); }
            if (points != null && StoreFactory == null && Bootstrap?.Wallet != null)
                points.text = "Wallet  " + Bootstrap.Wallet.DisplayText + " points";
        }
        private enum ScreenMode { Training, Race, Lobby }
        private bool Visible => (trainingScreen != null && trainingScreen.activeSelf)
            || (raceScreen != null && raceScreen.activeSelf)
            || (lobbyScreen != null && lobbyScreen.activeSelf);

        public void OpenTraining() => ShowScreen(ScreenMode.Training);
        public void OpenRace() => ShowScreen(ScreenMode.Race);
        public void OpenLobby() => ShowScreen(ScreenMode.Lobby);
        private void ShowScreen(ScreenMode mode)
        {
            if (!Visible)
            {
                foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                    if (map.enabled) { map.Suspend(this); maps.Add(map); }
            }
            if (mode != ScreenMode.Race) race = null;
            if (trainingScreen != null) trainingScreen.SetActive(mode == ScreenMode.Training);
            if (raceScreen != null) raceScreen.SetActive(mode == ScreenMode.Race);
            if (lobbyScreen != null) lobbyScreen.SetActive(mode == ScreenMode.Lobby);
            var active = mode == ScreenMode.Training ? trainingScreen : mode == ScreenMode.Race ? raceScreen : lobbyScreen;
            if (active != null) active.transform.SetAsLastSibling();
            RenderRace(); RenderStats(); RenderLobby();
            _ = RefreshAsync();
        }

        public void Close()
        {
            generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            busy = false; race = null;
            LeaveRoomInternal();
            if (trainingScreen != null) trainingScreen.SetActive(false);
            if (raceScreen != null) raceScreen.SetActive(false);
            if (lobbyScreen != null) lobbyScreen.SetActive(false);
            foreach (var map in maps) if (map != null) map.Resume(this);
            maps.Clear();
        }
        private void ResetAccount()
        {
            generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            busy = false; race = null; snapshot = null; pendingTraining = null;
            LeaveRoomInternal();
            owner = CurrentOwner; initialized = true;
            if (Application.isPlaying && !string.IsNullOrEmpty(owner))
            {
                try { pendingTraining = JsonUtility.FromJson<PendingTraining>(PlayerPrefs.GetString(PendingKey, "")); }
                catch (Exception) { pendingTraining = null; }
                if (pendingTraining != null && (!Guid.TryParseExact(pendingTraining.id, "N", out _) || !Enum.IsDefined(typeof(DogStat), pendingTraining.stat)))
                    pendingTraining = null;
            }
            RenderStats(); RenderRace();
        }
        private string PendingKey => "dogTraining.pending." + owner;
        private void SavePending()
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(owner)) return;
            if (pendingTraining == null) PlayerPrefs.DeleteKey(PendingKey);
            else PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(pendingTraining));
            PlayerPrefs.Save();
        }

        public void RefreshTraining() { _ = RefreshAsync(); }
        internal async Task RefreshAsync()
        {
            if (busy) return;
            if (!initialized || owner != CurrentOwner) ResetAccount();
            busy = true;
            int request = generation;
            var token = lifetime.Token;
            trainingStatus.text = "Loading your dog…"; RenderStats();
            try
            {
                if (StoreFactory == null)
                {
                    if (Bootstrap == null) throw new InvalidOperationException("Open this feature from the walking screen.");
                    await Bootstrap.InitializeAsync();
                    if (!Current(request)) return;
                    Bootstrap.Wallet?.SynchronizeAccount();
                    if (owner != CurrentOwner) { ResetAccount(); request = generation; token = lifetime.Token; busy = true; }
                }
                if (string.IsNullOrEmpty(owner))
                {
                    snapshot = new DogStats();
                    trainingStatus.text = "Sign in to save your dog and train with wallet points. You can try a practice race.";
                    return;
                }
                if (store == null) store = StoreFactory != null ? await StoreFactory()
                    : new FirebaseDogTrainingStore(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
                if (!Current(request)) return;
                bool confirmedTraining = pendingTraining != null;
                var loaded = pendingTraining == null
                    ? await WaitAsync(store.LoadAsync(owner, token), token)
                    : await WaitAsync(store.TrainAsync(owner, pendingTraining.stat, pendingTraining.id, token), token);
                if (!Current(request)) return;
                snapshot = loaded.Copy(); pendingTraining = null; SavePending();
                trainingStatus.text = "Choose a training session. Each costs 20 points and adds 5 to one stat.";
                if (StoreFactory == null)
                {
                    await Bootstrap.Wallet.RefreshAsync();
                    if (confirmedTraining && Current(request)) _ = RefreshLeaderboardBalanceAsync(owner);
                }
            }
            catch (InvalidOperationException error)
            {
                if (Current(request)) { pendingTraining = null; SavePending(); trainingStatus.text = error.Message; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (Current(request)) trainingStatus.text = pendingTraining != null
                    ? "Training is awaiting confirmation. Tap Refresh to check it; the same session won't charge twice."
                    : "Couldn't load your dog. Check your connection and tap Refresh.";
                Debug.LogWarning("Dog loading pending: " + error.GetType().Name);
            }
            finally { if (Current(request)) { busy = false; RenderStats(); RenderRace(); } }
        }

        public void TrainSpeed() { _ = TrainAsync(DogStat.Speed); }
        public void TrainStamina() { _ = TrainAsync(DogStat.Stamina); }
        public void TrainAcceleration() { _ = TrainAsync(DogStat.Acceleration); }
        internal async Task TrainAsync(DogStat stat)
        {
            if (busy || snapshot == null || string.IsNullOrEmpty(owner) || owner != CurrentOwner || pendingTraining != null) return;
            if (snapshot.Get(stat) >= DogStats.MaximumValue) return;
            busy = true;
            int request = generation;
            pendingTraining = new PendingTraining { id = Guid.NewGuid().ToString("N"), stat = stat }; SavePending();
            trainingStatus.text = "Training your dog…"; RenderStats();
            try
            {
                var next = await WaitAsync(store.TrainAsync(owner, stat, pendingTraining.id, lifetime.Token), lifetime.Token);
                if (!Current(request)) return;
                snapshot = next.Copy(); pendingTraining = null; SavePending();
                trainingStatus.text = stat + " improved by 5. Training saved.";
                if (StoreFactory == null)
                {
                    await Bootstrap.Wallet.RefreshAsync();
                    if (Current(request)) _ = RefreshLeaderboardBalanceAsync(owner);
                }
            }
            catch (InvalidOperationException error)
            {
                if (Current(request)) { pendingTraining = null; SavePending(); trainingStatus.text = error.Message; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (Current(request)) trainingStatus.text = "Training is awaiting confirmation. Tap Refresh to retry this session without paying twice.";
                Debug.LogWarning("Dog training pending: " + error.GetType().Name);
            }
            finally { if (Current(request)) { busy = false; RenderStats(); } }
        }
        private static async Task RefreshLeaderboardBalanceAsync(string uid)
        {
            try { await WalkingDog.Leaderboards.FirebaseLeaderboardWriter.SynchronizePointsBalanceAsync(FirebaseFirestore.DefaultInstance, uid); }
            catch (Exception error) { Debug.LogWarning("Training leaderboard balance pending: " + error.GetType().Name); }
        }

        public void StartRace()
        {
            if (busy || snapshot == null || owner != CurrentOwner || (race != null && !race.Completed)) return;
            var entries = DogRaceSimulation.PracticeField(snapshot, CurrentEquippedItemId);
            race = new DogRaceSimulation(entries, ++raceNumber);
            RenderRace();
        }

        public void CreateRoom() { _ = CreateRoomAsync(); }
        public void JoinRoom() { _ = JoinRoomAsync(); }
        public void LeaveRoom() { LeaveRoomInternal(); RenderLobby(); }
        public void StartLobbyRace() { _ = StartLobbyRaceAsync(); }

        private async Task EnsureLobbyServiceAsync()
        {
            if (lobbyService != null) return;
            lobbyService = LobbyServiceFactory != null
                ? await LobbyServiceFactory()
                : new FirebaseRaceLobbyService(FirebaseFirestore.DefaultInstance);
        }

        internal async Task CreateRoomAsync()
        {
            if (busy || string.IsNullOrEmpty(owner)) return;
            busy = true;
            if (lobbyStatus != null) lobbyStatus.text = "Creating room…";
            RenderLobby();
            try
            {
                await EnsureLobbyServiceAsync();
                var created = await lobbyService.CreateRoomAsync(owner, CurrentPlayerName, snapshot ?? new DogStats(), CurrentEquippedItemId, lifetime.Token);
                SubscribeToRoom(created);
                if (lobbyStatus != null) lobbyStatus.text = "Room created! Share the code to invite friends.";
            }
            catch (Exception ex)
            {
                if (lobbyStatus != null) lobbyStatus.text = "Couldn't create room: " + ex.Message;
                Debug.LogWarning("Create room error: " + ex.Message);
            }
            finally
            {
                busy = false;
                RenderLobby();
            }
        }

        internal async Task JoinRoomAsync()
        {
            if (busy || string.IsNullOrEmpty(owner)) return;
            string code = roomCodeInput != null ? roomCodeInput.text : "";
            string norm = RaceRoomCodes.Normalize(code);
            if (string.IsNullOrEmpty(norm))
            {
                if (lobbyStatus != null) lobbyStatus.text = "Please enter a valid room code.";
                return;
            }
            busy = true;
            if (lobbyStatus != null) lobbyStatus.text = "Joining room " + RaceRoomCodes.Format(norm) + "…";
            RenderLobby();
            try
            {
                await EnsureLobbyServiceAsync();
                var joined = await lobbyService.JoinRoomAsync(norm, owner, CurrentPlayerName, snapshot ?? new DogStats(), CurrentEquippedItemId, lifetime.Token);
                SubscribeToRoom(joined);
                if (lobbyStatus != null) lobbyStatus.text = "Joined room!";
            }
            catch (Exception ex)
            {
                if (lobbyStatus != null) lobbyStatus.text = ex.Message;
                Debug.LogWarning("Join room error: " + ex.Message);
            }
            finally
            {
                busy = false;
                RenderLobby();
            }
        }

        private void SubscribeToRoom(RaceRoom room)
        {
            roomSubscription?.Dispose();
            currentRoom = room;
            RenderLobby();
            try
            {
                roomSubscription = lobbyService.ListenRoom(room.RoomCode, OnRoomUpdated, OnRoomError);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Listen room error: " + ex.Message);
            }
        }

        private void OnRoomUpdated(RaceRoom updated)
        {
            if (updated == null || updated.IsClosed)
            {
                LeaveRoomInternal();
                if (lobbyStatus != null) lobbyStatus.text = "The room was closed.";
                RenderLobby();
                return;
            }

            currentRoom = updated;
            if (updated.IsRacing)
            {
                roomSubscription?.Dispose();
                roomSubscription = null;
                var field = updated.BuildSimulationField();
                race = new DogRaceSimulation(field, updated.Seed, updated.TrackLength);
                ShowScreen(ScreenMode.Race);
                return;
            }

            RenderLobby();
        }

        private void OnRoomError(Exception ex)
        {
            Debug.LogWarning("Room subscription error: " + ex?.Message);
        }

        private void LeaveRoomInternal()
        {
            roomSubscription?.Dispose();
            roomSubscription = null;
            if (currentRoom != null && lobbyService != null && !string.IsNullOrEmpty(owner))
            {
                var code = currentRoom.RoomCode;
                _ = lobbyService.LeaveRoomAsync(code, owner, CancellationToken.None);
            }
            currentRoom = null;
        }

        internal async Task StartLobbyRaceAsync()
        {
            if (busy || currentRoom == null || !currentRoom.CanStart(owner)) return;
            busy = true;
            if (lobbyStatus != null) lobbyStatus.text = "Starting race…";
            RenderLobby();
            try
            {
                await EnsureLobbyServiceAsync();
                int seed = UnityEngine.Random.Range(1000, 999999);
                await lobbyService.StartRaceAsync(currentRoom.RoomCode, owner, seed, lifetime.Token);
            }
            catch (Exception ex)
            {
                if (lobbyStatus != null) lobbyStatus.text = "Couldn't start race: " + ex.Message;
                Debug.LogWarning("Start race error: " + ex.Message);
                busy = false;
                RenderLobby();
            }
        }

        internal void RenderLobby()
        {
            if (lobbyScreen == null) return;
            bool inRoom = currentRoom != null;
            if (lobbyFindSection != null) lobbyFindSection.SetActive(!inRoom);
            if (lobbyRoomSection != null) lobbyRoomSection.SetActive(inRoom);

            if (!inRoom)
            {
                if (lobbyStatus != null && !busy)
                {
                    lobbyStatus.text = string.IsNullOrEmpty(owner)
                        ? "Sign in to create or join multiplayer races."
                        : "Enter a room code or create a new room to race with friends.";
                }
                if (createRoomButton != null) createRoomButton.interactable = !busy && !string.IsNullOrEmpty(owner);
                if (joinRoomButton != null) joinRoomButton.interactable = !busy && !string.IsNullOrEmpty(owner);
                if (practiceRaceButton != null) practiceRaceButton.interactable = !busy;
            }
            else
            {
                if (lobbyRoomCodeText != null)
                    lobbyRoomCodeText.text = "Room  " + RaceRoomCodes.Format(currentRoom.RoomCode);

                if (lobbyEntrantRows != null)
                {
                    for (int i = 0; i < lobbyEntrantRows.Length; i++)
                    {
                        if (i < currentRoom.Entrants.Count)
                        {
                            var entrant = currentRoom.Entrants[i];
                            string role = entrant.IsHost ? " (Host)" : "";
                            string you = entrant.PlayerId == owner ? " [You]" : "";
                            lobbyEntrantRows[i].text = $"{i + 1}.  {entrant.DisplayName}{you}{role}\n     Speed {entrant.Stats.speed} · Stamina {entrant.Stats.stamina} · Accel {entrant.Stats.acceleration}";
                        }
                        else
                        {
                            lobbyEntrantRows[i].text = $"{i + 1}.  [Open slot — AI runner fills lane]";
                        }
                    }
                }

                if (startLobbyRaceButton != null)
                {
                    bool isHost = currentRoom.HostUid == owner;
                    startLobbyRaceButton.interactable = !busy && isHost;
                    var label = startLobbyRaceButton.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = isHost ? "Start Race" : "Waiting for Host…";
                }
                if (leaveRoomButton != null) leaveRoomButton.interactable = !busy;
            }
        }

        internal static GachaItem ResolveGachaItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            var manager = FindFirstObjectByType<GachaManager>();
            if (manager != null && manager.itemCatalog != null)
            {
                return manager.itemCatalog.FirstOrDefault(i => i != null && i.ItemId == itemId);
            }
            return null;
        }

        internal void RenderStats()
        {
            dogSummary.text = snapshot == null ? "Your dog is loading" : $"Your dog  ·  {snapshot.trainingCount} training sessions";
            foreach (var row in stats)
            {
                int value = snapshot?.Get(row.stat) ?? 0;
                row.value.text = snapshot == null ? "— / 100" : value + " / 100";
                row.bar.SetValueWithoutNotify(value);
                row.train.interactable = !busy && !string.IsNullOrEmpty(owner) && snapshot != null
                    && value < DogStats.MaximumValue && pendingTraining == null;
            }
            refreshTraining.interactable = !busy;
            if (string.IsNullOrEmpty(owner)) points.text = "Sign in to use wallet points";
            else if (StoreFactory != null) points.text = "Wallet points";
            else points.text = "Wallet  " + (Bootstrap?.Wallet?.DisplayText ?? "loading") + " points";
        }

        internal void RenderRace()
        {
            raceSummary.text = snapshot == null ? "Load your dog to race."
                : $"Your dog  ·  Speed {snapshot.speed}  ·  Stamina {snapshot.stamina}  ·  Acceleration {snapshot.acceleration}";
            startRace.interactable = !busy && snapshot != null && (race == null || race.Completed);
            startRace.GetComponentInChildren<TMP_Text>().text = race == null ? "Start race" : race.Completed ? "Race again" : "Racing…";
            var entries = race == null ? DogRaceSimulation.PracticeField(snapshot ?? new DogStats(), CurrentEquippedItemId) : race.Runners.Select(r => r.Entry).ToList();
            for (int i = 0; i < lanes.Length; i++)
            {
                double progress = race == null ? 0 : race.Runners[i].Distance / race.Length;
                var marker = lanes[i].marker;
                marker.anchorMin = marker.anchorMax = new Vector2(.08f + .86f * (float)progress, .5f);
                var entry = entries[i];
                lanes[i].details.text = entry.Name + $"  ·  {entry.Stats.speed} / {entry.Stats.stamina} / {entry.Stats.acceleration}";

                var gacha = ResolveGachaItem(entry.EquippedItemId);
                if (lanes[i].iconImage != null)
                {
                    if (gacha != null && gacha.Icon_img != null)
                    {
                        lanes[i].iconImage.sprite = gacha.Icon_img;
                        lanes[i].iconImage.gameObject.SetActive(true);
                        var dogIcon = lanes[i].marker.GetComponent<DogIconGraphic>();
                        if (dogIcon != null) dogIcon.enabled = false;
                    }
                    else
                    {
                        lanes[i].iconImage.gameObject.SetActive(false);
                        var dogIcon = lanes[i].marker.GetComponent<DogIconGraphic>();
                        if (dogIcon != null) dogIcon.enabled = true;
                    }
                }
            }
            raceStatus.text = race == null ? "200 m practice race · Three simulated training partners"
                : race.Completed ? "Race finished" : $"{race.Elapsed:0.0}s  ·  Dogs run automatically from their stats";
            results.text = race == null ? "Speed sets pace. Acceleration helps the start. Stamina delays fatigue."
                : race.Completed ? string.Join("\n", race.Standings().Select((runner, index) => $"{index + 1}.  {runner.Entry.Name}  ·  {runner.FinishTime:0.00}s"))
                : "Leading: " + race.Standings()[0].Entry.Name;
        }
        private bool Current(int request) => this != null && request == generation && owner == CurrentOwner;
        private static async Task<T> WaitAsync<T>(Task<T> operation, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var timer = Task.Delay(TimeSpan.FromSeconds(30), deadline.Token);
                if (await Task.WhenAny(operation, timer) != operation)
                { Observe(operation); token.ThrowIfCancellationRequested(); throw new TimeoutException("Dog training acknowledgement pending."); }
                deadline.Cancel(); var value = await operation; token.ThrowIfCancellationRequested(); return value;
            }
        }
        private static async void Observe(Task operation) { try { await operation; } catch (Exception) { } }
        private void OnDestroy()
        {
            generation++; lifetime.Cancel(); lifetime.Dispose();
            foreach (var map in maps) if (map != null) map.Resume(this);
            maps.Clear();
        }
    }
}
