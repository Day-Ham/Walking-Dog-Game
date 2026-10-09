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
        }
        [Serializable] private sealed class PendingTraining { public string id; public DogStat stat; }

        [SerializeField] private GameObject trainingScreen, raceScreen;
        [SerializeField] private TMP_Text trainingStatus, points, dogSummary, raceStatus, raceSummary, results;
        [SerializeField] private StatRow[] stats;
        [SerializeField] private RaceLane[] lanes;
        [SerializeField] private Button startRace, refreshTraining;
        private readonly List<OpenFreeMapWebViewMap> maps = new List<OpenFreeMapWebViewMap>();
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private IDogTrainingStore store;
        private PendingTraining pendingTraining;
        private string owner = "";
        private bool busy, initialized;
        private int generation, raceNumber;
        private DogRaceSimulation race;
        private DogStats snapshot;
        internal Func<string> OwnerReader;
        internal Func<Task<IDogTrainingStore>> StoreFactory;
        internal DogStats Snapshot => snapshot?.Copy();
        internal DogRaceSimulation Race => race;
        internal string TrainingStatus => trainingStatus.text;
        internal bool IsBusy => busy;
        private FirebaseWalkBootstrap Bootstrap => StepCountAndGpsManager.Instance?.GetComponent<FirebaseWalkBootstrap>();
        private string CurrentOwner => OwnerReader != null ? OwnerReader() : Bootstrap?.Wallet?.Owner ?? "";

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
        private bool Visible => trainingScreen.activeSelf || raceScreen.activeSelf;

        public void OpenTraining() { Open(false); }
        public void OpenRace() { Open(true); }
        private void Open(bool racing)
        {
            if (!Visible)
            {
                foreach (var map in FindObjectsByType<OpenFreeMapWebViewMap>(FindObjectsSortMode.None))
                    if (map.enabled) { map.Suspend(this); maps.Add(map); }
            }
            race = null;
            trainingScreen.SetActive(!racing); raceScreen.SetActive(racing);
            (racing ? raceScreen : trainingScreen).transform.SetAsLastSibling();
            RenderRace(); RenderStats();
            _ = RefreshAsync();
        }

        public void Close()
        {
            generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            busy = false; race = null;
            trainingScreen.SetActive(false); raceScreen.SetActive(false);
            foreach (var map in maps) if (map != null) map.Resume(this);
            maps.Clear();
        }
        private void ResetAccount()
        {
            generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            busy = false; race = null; snapshot = null; pendingTraining = null;
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
            race = new DogRaceSimulation(DogRaceSimulation.PracticeField(snapshot), ++raceNumber);
            RenderRace();
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
            var entries = race == null ? DogRaceSimulation.PracticeField(snapshot ?? new DogStats()) : race.Runners.Select(r => r.Entry).ToList();
            for (int i = 0; i < lanes.Length; i++)
            {
                double progress = race == null ? 0 : race.Runners[i].Distance / race.Length;
                var marker = lanes[i].marker;
                marker.anchorMin = marker.anchorMax = new Vector2(.08f + .86f * (float)progress, .5f);
                var entry = entries[i];
                lanes[i].details.text = entry.Name + $"  ·  {entry.Stats.speed} / {entry.Stats.stamina} / {entry.Stats.acceleration}";
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
