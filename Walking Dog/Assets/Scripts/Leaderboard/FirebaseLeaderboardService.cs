using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    // Plain service: no GameObjects or UI references. Reconciles the owner's
    // saved cloud walks when loading; rules validate every score transaction.
    // Create/call/dispose on Unity's main thread after Firebase initialization.
    public sealed class FirebaseLeaderboardService : ILeaderboardService
    {
        public const int PageSize = 50;
        private const string PlayersPath = "leaderboards/allTime/players";
        private readonly Func<string> currentUser; // delegates 
        private readonly Func<string, LeaderboardMetric, Task<LeaderboardSnapshot>> read;
        private readonly Func<string, string, Task> saveName;
        private readonly TimeSpan timeout;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private FirebaseAuth auth;
        private int authRevision;
        private bool disposed;
        private FirebaseFirestore firestore;
        private FirebaseLeaderboardWriter.ReconciliationProgress reconciliation = new FirebaseLeaderboardWriter.ReconciliationProgress();
        public string AuthenticatedUserId => disposed ? "" : currentUser() ?? "";

        public FirebaseLeaderboardService(FirebaseAuth auth, FirebaseFirestore firestore)
            : this(() => auth.CurrentUser?.UserId,
                (uid, metric) => FetchAsync(firestore, uid, metric),
                (uid, name) => FirebaseLeaderboardWriter.SaveNameAsync(firestore, uid, name))
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (firestore == null) throw new ArgumentNullException(nameof(firestore));
            this.auth = auth;
            this.firestore = firestore;
            auth.StateChanged += OnAuthChanged;
        }

        internal FirebaseLeaderboardService(Func<string> currentUser,
            Func<string, LeaderboardMetric, Task<LeaderboardSnapshot>> read,
            Func<string, string, Task> saveName, TimeSpan? timeout = null) // constructor for leaderboard service
        {
            this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.saveName = saveName ?? throw new ArgumentNullException(nameof(saveName));
            this.timeout = timeout ?? TimeSpan.FromSeconds(20);
        }

        public static async Task<FirebaseLeaderboardService> CreateAsync(FirebaseWalkBootstrap bootstrap) //create async task when loading the leaderboard service, check if firebase is ready and initialized
        {
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));
            await bootstrap.InitializeAsync();
            if (bootstrap == null || !bootstrap.IsReady) throw new InvalidOperationException("Firebase is unavailable.");
            return new FirebaseLeaderboardService(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
        }

        public async Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, CancellationToken cancellationToken) // the load async itslef 
        {
            if (metric != LeaderboardMetric.Distance && metric != LeaderboardMetric.Steps)
                throw new ArgumentOutOfRangeException(nameof(metric));
            var uid = RequireUser(cancellationToken);
            var revision = authRevision;
            LeaderboardSnapshot result;
            try { result = await WaitAsync(LoadWithReconciliationAsync(uid, metric, cancellationToken), cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !disposed && revision == authRevision)
            { throw new TimeoutException("Leaderboard sync is taking longer. Refresh to continue."); }
            CheckAccount(uid, revision, cancellationToken);
            return result;
        }

        private async Task<LeaderboardSnapshot> LoadWithReconciliationAsync(string uid, LeaderboardMetric metric, CancellationToken token) // load with reconciliation, if the user has any unsynced data, it will reconcile it with the server before loading the leaderboard
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                // Stop paged work on timeout even though native reads cannot be cancelled.
                linked.CancelAfter(timeout);
                if (firestore != null) await FirebaseLeaderboardWriter.ReconcileAsync(firestore, uid, reconciliation, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return await read(uid, metric);
            }
        }

        public async Task SaveDisplayNameAsync(string displayName, CancellationToken cancellationToken) //save the display name no not stored to firestore
        {
            var uid = RequireUser(cancellationToken);
            if (!LeaderboardNames.IsValid(displayName))
                throw new ArgumentException("Use 3–24 letters, numbers, spaces, underscores or hyphens; start with a letter or number.", nameof(displayName));
            var revision = authRevision;
            await WaitAsync(AsResult(saveName(uid, displayName)), cancellationToken);
            CheckAccount(uid, revision, cancellationToken);
        }

        private string RequireUser(CancellationToken cancellationToken) // need to be authenticated to view the leaderboard, if not throw an exception
        {
            if (disposed) throw new ObjectDisposedException(nameof(FirebaseLeaderboardService));
            cancellationToken.ThrowIfCancellationRequested();
            var uid = AuthenticatedUserId;
            if (string.IsNullOrWhiteSpace(uid)) throw new InvalidOperationException("Sign in to view the leaderboard.");
            return uid;
        }

        private void CheckAccount(string uid, int revision, CancellationToken token) //
        {
            token.ThrowIfCancellationRequested();
            if (disposed || uid != AuthenticatedUserId || revision != authRevision)
                throw new OperationCanceledException("The leaderboard account changed.");
        }

        private async Task<T> WaitAsync<T>(Task<T> task, CancellationToken token)
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var deadline = Task.Delay(timeout, wait.Token);
                if (await Task.WhenAny(task, deadline) != task)
                {
                    Observe(task);
                    wait.Token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Leaderboard request timed out. Check your connection and retry.");
                }
                var result = await task;
                wait.Token.ThrowIfCancellationRequested();
                wait.Cancel();
                return result;
            }
        }

        private static async Task<LeaderboardSnapshot> FetchAsync(FirebaseFirestore firestore, string uid, LeaderboardMetric metric) //fetch function async from database
        {
            var field = metric == LeaderboardMetric.Distance ? "totalDistanceMeters" : "totalSteps";
            // Firestore's implicit document-ID tie breaker follows this descending
            // order. No composite index or per-walk downloads are needed.
            var query = firestore.Collection(PlayersPath).OrderByDescending(field).Limit(PageSize);
            var snapshot = await query.GetSnapshotAsync(Source.Server);
            var entries = new List<LeaderboardEntry>();
            foreach (var document in snapshot.Documents)
            {
                if (!LeaderboardEntry.TryParse(document.Id, document.ToDictionary(), out var entry))
                    throw new InvalidOperationException("Leaderboard data is invalid.");
                entries.Add(entry);
            }
            var own = entries.FirstOrDefault(entry => entry.PlayerId == uid);
            if (own == null)
            {
                var document = await firestore.Document($"{PlayersPath}/{uid}").GetSnapshotAsync(Source.Server);
                if (document.Exists && !LeaderboardEntry.TryParse(document.Id, document.ToDictionary(), out own))
                    throw new InvalidOperationException("Player leaderboard data is invalid.");
            }
            return new LeaderboardSnapshot(metric, uid, entries.AsReadOnly(), own);
        }

        private void OnAuthChanged(object sender, EventArgs args) // update auth condition depending on user
        {
            authRevision++;
            reconciliation = new FirebaseLeaderboardWriter.ReconciliationProgress();
        }
        private static async Task<bool> AsResult(Task task) { await task; return true; }
        private static async void Observe(Task task) { try { await task; } catch (Exception) { } }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (auth != null) auth.StateChanged -= OnAuthChanged;
            lifetime.Cancel();
            lifetime.Dispose();
            // Shared Firebase instances belong to the existing bootstrap.
        }
    }
}
