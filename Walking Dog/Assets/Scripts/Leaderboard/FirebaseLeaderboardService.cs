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
    public sealed partial class FirebaseLeaderboardService : ILeaderboardService, IFriendsService, IPlayerProfileService
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

        // Creates a new leaderboard service using default Firebase Auth and Firestore instances.
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

        // Internal constructor supporting custom user delegates and fetch implementations for testing.
        internal FirebaseLeaderboardService(Func<string> currentUser,
            Func<string, LeaderboardMetric, Task<LeaderboardSnapshot>> read,
            Func<string, string, Task> saveName, TimeSpan? timeout = null)
        {
            this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.saveName = saveName ?? throw new ArgumentNullException(nameof(saveName));
            this.timeout = timeout ?? TimeSpan.FromSeconds(20);
        }

        // Asynchronously initializes Firebase and instantiates the leaderboard service.
        public static async Task<FirebaseLeaderboardService> CreateAsync(FirebaseWalkBootstrap bootstrap)
        {
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));
            await bootstrap.InitializeAsync();
            if (bootstrap == null || !bootstrap.IsReady) throw new InvalidOperationException("Firebase is unavailable.");
            return new FirebaseLeaderboardService(FirebaseAuth.DefaultInstance, FirebaseFirestore.DefaultInstance);
        }

        // Loads the global leaderboard snapshot for the specified metric.
        public async Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, CancellationToken cancellationToken)
            => await LoadAsync(metric, LeaderboardScope.Global, cancellationToken);

        // Loads either global or friends leaderboard rankings for the specified metric with account verification.
        public async Task<LeaderboardSnapshot> LoadAsync(LeaderboardMetric metric, LeaderboardScope scope, CancellationToken cancellationToken)
        {
            if (metric != LeaderboardMetric.Distance && metric != LeaderboardMetric.Steps)
                throw new ArgumentOutOfRangeException(nameof(metric));
            if (scope != LeaderboardScope.Global && scope != LeaderboardScope.Friends)
                throw new ArgumentOutOfRangeException(nameof(scope));
            var uid = RequireUser(cancellationToken);
            var revision = authRevision;
            LeaderboardSnapshot result;
            try { result = await WaitAsync(LoadWithReconciliationAsync(uid, metric, scope, cancellationToken), cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !disposed && revision == authRevision)
            { throw new TimeoutException("Leaderboard sync is taking longer. Refresh to continue."); }
            CheckAccount(uid, revision, cancellationToken);
            return result;
        }

        // Reconciles local walks and reads the current leaderboard snapshot for the player.
        private async Task<LeaderboardSnapshot> LoadWithReconciliationAsync(string uid, LeaderboardMetric metric, LeaderboardScope scope, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                // Stop paged work on timeout even though native reads cannot be cancelled.
                linked.CancelAfter(timeout);
                if (firestore != null)
                {
                    await RegisterCodeAsync(uid, authRevision, linked.Token);
                    await FirebaseLeaderboardWriter.ReconcileAsync(firestore, uid, reconciliation, linked.Token);
                }
                linked.Token.ThrowIfCancellationRequested();
                var board = scope == LeaderboardScope.Friends
                    ? await FetchFriendsBoardAsync(uid, metric, linked.Token) : await read(uid, metric);
                return firestore == null ? board : await WithPhotosAsync(board, linked.Token);
            }
        }

        // Validates and saves the player's display name to Firestore.
        public async Task SaveDisplayNameAsync(string displayName, CancellationToken cancellationToken)
        {
            var uid = RequireUser(cancellationToken);
            if (!LeaderboardNames.IsValid(displayName))
                throw new ArgumentException("Use 3–24 letters, numbers, spaces, underscores or hyphens; start with a letter or number.", nameof(displayName));
            var revision = authRevision;
            await WaitAsync(AsResult(saveName(uid, displayName)), cancellationToken);
            CheckAccount(uid, revision, cancellationToken);
        }

        // Ensures the service is active and the user is authenticated, returning the user ID.
        private string RequireUser(CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FirebaseLeaderboardService));
            cancellationToken.ThrowIfCancellationRequested();
            var uid = AuthenticatedUserId;
            if (string.IsNullOrWhiteSpace(uid)) throw new InvalidOperationException("Sign in to view the leaderboard.");
            return uid;
        }

        // Verifies that the authenticated account has not changed during an asynchronous operation.
        private void CheckAccount(string uid, int revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (disposed || uid != AuthenticatedUserId || revision != authRevision)
                throw new OperationCanceledException("The leaderboard account changed.");
        }

        // Awaits a task while enforcing timeout and cancellation token constraints.
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

        // Queries the top leaderboard records from Firestore and retrieves the player's own rank entry if not in top list.
        private static async Task<LeaderboardSnapshot> FetchAsync(FirebaseFirestore firestore, string uid, LeaderboardMetric metric)
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

        // Resets reconciliation progress and increments auth revision when Firebase Auth state changes.
        private void OnAuthChanged(object sender, EventArgs args)
        {
            authRevision++;
            reconciliation = new FirebaseLeaderboardWriter.ReconciliationProgress();
        }

        // Adapts a non-generic Task into a Task<bool> for unified timeout handling.
        private static async Task<bool> AsResult(Task task) { await task; return true; }

        // Observes a background task to suppress unobserved task exceptions when timed out.
        private static async void Observe(Task task) { try { await task; } catch (Exception) { } }

        // Cleans up event subscriptions and cancels ongoing operations.
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
