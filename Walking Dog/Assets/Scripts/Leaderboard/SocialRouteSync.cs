using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    // Completed local walks are the durable retry source. A fresh installation
    // keeps routes already shared by another phone; GPS is never reconstructed.
    internal sealed class SocialRouteSync
    {
        private readonly Func<string> currentUser;
        private readonly Func<List<StepCountAndGpsManager.SavedWalkSession>> walks;
        private readonly Func<string, Task<bool>> sharingEnabled;
        private readonly Func<string, string, string, CancellationToken, Task<bool>> upload;
        private readonly HashSet<string> acknowledged = new HashSet<string>();
        private string owner = "";
        private bool running;

        internal SocialRouteSync(Func<string> currentUser,
            Func<List<StepCountAndGpsManager.SavedWalkSession>> walks,
            Func<string, Task<bool>> sharingEnabled,
            Func<string, string, string, CancellationToken, Task<bool>> upload)
        {
            this.currentUser = currentUser; this.walks = walks;
            this.sharingEnabled = sharingEnabled; this.upload = upload;
        }

        internal static SocialRouteSync Create(FirebaseFirestore db, Func<string> currentUser, LocalWalkRepository repository)
            => new SocialRouteSync(currentUser, repository.LoadAll,
                async uid => {
                    var settings = await db.Document($"socialProfiles/{uid}").GetSnapshotAsync(Source.Server);
                    return settings.Exists && settings.TryGetValue("shareActivity", out bool enabled) && enabled;
                },
                (uid, id, json, token) => db.RunTransactionAsync(async tx => {
                    var settings = await tx.GetSnapshotAsync(db.Document($"socialProfiles/{uid}"));
                    var summary = await tx.GetSnapshotAsync(db.Document($"users/{uid}/walks/{id}"));
                    var reference = db.Document($"users/{uid}/sharedRoutes/{id}");
                    var existing = await tx.GetSnapshotAsync(reference);
                    token.ThrowIfCancellationRequested();
                    if (currentUser() != uid) throw new OperationCanceledException();
                    // Read sharing inside the transaction, so OFF cannot race an
                    // old background uploader. Never overwrite a cloud route.
                    if (!settings.Exists || !settings.TryGetValue("shareActivity", out bool enabled) || !enabled || !summary.Exists)
                        return false;
                    if (!existing.Exists) tx.Set(reference, new Dictionary<string, object> {
                        ["schemaVersion"] = 1, ["walkId"] = id,
                        ["routeJson"] = json, ["updatedAt"] = FieldValue.ServerTimestamp
                    });
                    return true;
                }));

        internal async Task SyncAsync(CancellationToken token)
        {
            if (running || string.IsNullOrEmpty(currentUser())) return;
            running = true;
            var uid = currentUser();
            try
            {
                if (uid != owner) { owner = uid; acknowledged.Clear(); }
                Check(uid, token);
                if (!await WaitAsync(sharingEnabled(uid), token)) return;
                Check(uid, token);
                Exception failure = null;
                foreach (var walk in walks())
                {
                    Check(uid, token);
                    if (walk.ownerUserId != uid || walk.sync.state != WalkUploadState.Synced
                        || walk.routePoints == null || walk.routePoints.Count == 0 || acknowledged.Contains(walk.id)) continue;
                    try
                    {
                        var json = SocialRouteCodec.Encode(walk.routePoints);
                        if (!await WaitAsync(upload(uid, walk.id, json, token), token)) return;
                        Check(uid, token);
                        acknowledged.Add(walk.id);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { failure = error; } // One failed route must not block the others.
                }
                if (failure != null) throw new InvalidOperationException("Automatic route sharing is pending; it will retry.", failure);
            }
            finally { running = false; }
        }

        private void Check(string uid, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (currentUser() != uid) throw new OperationCanceledException();
        }

        private static async Task<T> WaitAsync<T>(Task<T> operation, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(30), deadline.Token);
                if (await Task.WhenAny(operation, delay) != operation)
                {
                    Observe(operation);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Route sharing acknowledgement is pending.");
                }
                deadline.Cancel();
                var result = await operation;
                token.ThrowIfCancellationRequested();
                return result;
            }
        }

        private static async void Observe(Task task) { try { await task; } catch (Exception) { } }
    }
}
