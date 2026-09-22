using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    public sealed partial class FirebaseLeaderboardService
    {
        public Task<string> GetFriendCodeAsync(CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                var owner = firestore.Document($"friendCodeOwners/{uid}");
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    var candidate = FriendCodes.Create();
                    var code = await firestore.RunTransactionAsync(async transaction =>
                    {
                        var existing = await transaction.GetSnapshotAsync(owner);
                        if (existing.Exists) return existing.GetValue<string>("code");
                        var lookup = firestore.Document($"friendCodeLookup/{candidate}");
                        var claimed = await transaction.GetSnapshotAsync(lookup);
                        CheckAccount(uid, revision, work);
                        if (claimed.Exists) return null;
                        transaction.Set(owner, new Dictionary<string, object> { ["code"] = candidate });
                        transaction.Set(lookup, new Dictionary<string, object> { ["playerId"] = uid });
                        return candidate;
                    });
                    if (code != null) return FriendCodes.Format(code);
                }
                throw new FriendRequestException("Couldn't create a friend code. Please try again.");
            }, token);

        public Task<IReadOnlyList<FriendEntry>> LoadFriendsAsync(CancellationToken token)
            => FriendOperationAsync<IReadOnlyList<FriendEntry>>(async (uid, revision, work) =>
            {
                await RegisterCodeAsync(uid, revision, work);
                var links = await ReadLinksAsync(uid, work);
                var result = new List<FriendEntry>();
                // Bound simultaneous reads. Missing scores do not hide a request.
                foreach (var group in Chunk(links, 10))
                {
                    CheckAccount(uid, revision, work);
                    var names = await Task.WhenAll(group.Select(d => firestore.Document($"friendCodes/{d.Id}").GetSnapshotAsync(Source.Server)));
                    for (int i = 0; i < group.Count; i++)
                    {
                        var link = group[i];
                        string name = names[i].Exists ? names[i].GetValue<string>("displayName") : FirebaseLeaderboardWriter.DefaultName(link.Id);
                        result.Add(new FriendEntry(link.Id, name, link.GetValue<string>("requestedBy"), link.GetValue<string>("status") == "accepted", ReadPhoto(names[i])));
                    }
                }
                return result.OrderBy(f => f.Accepted ? 2 : f.RequestedBy == uid ? 1 : 0)
                    .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.PlayerId, StringComparer.Ordinal).ToList().AsReadOnly();
            }, token);

        public async Task ChangeFriendAsync(string friendCode, FriendAction action, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(friendCode) || friendCode.Length > 128 || friendCode.Contains("/")
                || friendCode == "." || friendCode == ".." || friendCode.Any(char.IsWhiteSpace))
                throw new FriendRequestException("Paste a valid friend code.");
            if (!Enum.IsDefined(typeof(FriendAction), action)) throw new ArgumentOutOfRangeException(nameof(action));
            await FriendOperationAsync(async (uid, revision, work) =>
            {
                string targetId = friendCode;
                var shortCode = FriendCodes.Normalize(friendCode);
                if (action == FriendAction.Send && shortCode != null)
                {
                    var lookup = await firestore.Document($"friendCodeLookup/{shortCode}").GetSnapshotAsync(Source.Server);
                    if (lookup.Exists) targetId = lookup.GetValue<string>("playerId");
                }
                if (uid == targetId) throw new FriendRequestException("That is your own friend code.");
                await RegisterCodeAsync(uid, revision, work);
                CheckAccount(uid, revision, work);
                var own = firestore.Document($"friends/{uid}/links/{targetId}");
                var other = firestore.Document($"friends/{targetId}/links/{uid}");
                await firestore.RunTransactionAsync(async transaction =>
                {
                    var link = await transaction.GetSnapshotAsync(own);
                    var target = await transaction.GetSnapshotAsync(firestore.Document($"friendCodes/{targetId}"));
                    CheckAccount(uid, revision, work);
                    if (action == FriendAction.Remove)
                    {
                        if (link.Exists) { transaction.Delete(own); transaction.Delete(other); }
                        return;
                    }
                    if (!target.Exists) throw new FriendRequestException("Code not found. Ask your friend to open Manage friends first.");
                    if (action == FriendAction.Send && link.Exists)
                        throw new FriendRequestException(link.GetValue<string>("status") == "accepted" ? "You are already friends."
                            : link.GetValue<string>("requestedBy") == uid ? "Request already sent." : "This person has sent you a request. Accept it below.");
                    if (action == FriendAction.Accept && (!link.Exists || link.GetValue<string>("status") != "pending"
                        || link.GetValue<string>("requestedBy") == uid))
                        throw new FriendRequestException("This request is no longer available. Refresh your friends.");
                    var data = new Dictionary<string, object> {
                        ["requestedBy"] = action == FriendAction.Send ? uid : link.GetValue<string>("requestedBy"),
                        ["status"] = action == FriendAction.Send ? "pending" : "accepted", ["updatedAt"] = FieldValue.ServerTimestamp
                    };
                    transaction.Set(own, data);
                    transaction.Set(other, data);
                });
                return true;
            }, token);
        }

        private async Task<T> FriendOperationAsync<T>(Func<string, int, CancellationToken, Task<T>> operation, CancellationToken token)
        {
            var uid = RequireUser(token);
            var revision = authRevision;
            using (var work = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                work.CancelAfter(timeout);
                T result;
                try { result = await WaitAsync(operation(uid, revision, work.Token), token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && !disposed && revision == authRevision && uid == AuthenticatedUserId)
                { throw new TimeoutException("Friends request timed out. Refresh to check its status."); }
                CheckAccount(uid, revision, token);
                return result;
            }
        }

        private Task RegisterCodeAsync(string uid, int revision, CancellationToken token)
            => firestore.RunTransactionAsync(async transaction =>
            {
                var profile = await transaction.GetSnapshotAsync(firestore.Document($"leaderboardProfiles/{uid}"));
                var codeRef = firestore.Document($"friendCodes/{uid}");
                var existing = await transaction.GetSnapshotAsync(codeRef);
                CheckAccount(uid, revision, token);
                var name = profile.Exists ? profile.GetValue<string>("displayName") : FirebaseLeaderboardWriter.DefaultName(uid);
                var savedPhoto = ReadPhoto(existing);
                var photo = ProfilePhotos.IsUpload(savedPhoto) ? savedPhoto : ProfilePhotos.Normalize(auth?.CurrentUser?.PhotoUrl?.AbsoluteUri);
                if (!existing.Exists || existing.GetValue<string>("displayName") != name || ReadPhoto(existing) != photo)
                    transaction.Set(codeRef, new Dictionary<string, object> {
                        ["displayName"] = name, ["photoUrl"] = photo, ["updatedAt"] = FieldValue.ServerTimestamp
                    }, SetOptions.MergeAll);
            });

        public Task SaveProfilePhotoAsync(string uploadedPhoto, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                if (!string.IsNullOrEmpty(uploadedPhoto) && !ProfilePhotos.IsUpload(uploadedPhoto))
                    throw new ArgumentException("Choose a profile thumbnail smaller than 24 KB.", nameof(uploadedPhoto));
                await RegisterCodeAsync(uid, revision, work);
                CheckAccount(uid, revision, work);
                await firestore.Document($"friendCodes/{uid}").SetAsync(new Dictionary<string, object> {
                    ["photoUrl"] = string.IsNullOrEmpty(uploadedPhoto) ? ProfilePhotos.Normalize(auth?.CurrentUser?.PhotoUrl?.AbsoluteUri) : uploadedPhoto,
                    ["updatedAt"] = FieldValue.ServerTimestamp
                }, SetOptions.MergeAll);
                return true;
            }, token);

        private static string ReadPhoto(DocumentSnapshot profile)
            => profile.Exists && profile.TryGetValue<string>("photoUrl", out var photo) ? ProfilePhotos.Normalize(photo) : "";

        private async Task<LeaderboardSnapshot> WithPhotosAsync(LeaderboardSnapshot board, CancellationToken token)
        {
            var players = board.Entries.Select(p => p.PlayerId).Concat(new[] { board.CurrentPlayerId }).Distinct().ToList();
            var photos = new Dictionary<string, string>();
            foreach (var group in Chunk(players, 10))
            {
                token.ThrowIfCancellationRequested();
                var profiles = await Task.WhenAll(group.Select(id => firestore.Document($"friendCodes/{id}").GetSnapshotAsync(Source.Server)));
                for (int i = 0; i < group.Count; i++) photos[group[i]] = ReadPhoto(profiles[i]);
            }
            LeaderboardEntry Decorate(LeaderboardEntry entry) => entry == null ? null : new LeaderboardEntry(
                entry.PlayerId, entry.DisplayName, entry.TotalDistanceMeters, entry.TotalSteps, entry.CompletedWalkCount, photos[entry.PlayerId]);
            return new LeaderboardSnapshot(board.Metric, board.CurrentPlayerId, board.Entries.Select(Decorate).ToList().AsReadOnly(), Decorate(board.CurrentPlayer), board.Scope, photos[board.CurrentPlayerId]);
        }

        private async Task<List<DocumentSnapshot>> ReadLinksAsync(string uid, CancellationToken token)
        {
            var result = new List<DocumentSnapshot>();
            DocumentSnapshot cursor = null;
            for (;;)
            {
                token.ThrowIfCancellationRequested();
                Query query = firestore.Collection($"friends/{uid}/links").OrderBy(FieldPath.DocumentId).Limit(50);
                if (cursor != null) query = query.StartAfter(cursor);
                var page = (await query.GetSnapshotAsync(Source.Server)).Documents.ToList();
                result.AddRange(page);
                if (page.Count < 50) return result;
                cursor = page[page.Count - 1];
            }
        }

        private async Task<LeaderboardSnapshot> FetchFriendsBoardAsync(string uid, LeaderboardMetric metric, CancellationToken token)
        {
            var revision = authRevision;
            var links = await ReadLinksAsync(uid, token);
            var ids = links.Where(d => d.GetValue<string>("status") == "accepted").Select(d => d.Id).ToList();
            var players = new List<LeaderboardEntry>();
            foreach (var group in Chunk(ids.Concat(new[] { uid }).Distinct().ToList(), 10))
            {
                CheckAccount(uid, revision, token);
                var docs = await Task.WhenAll(group.Select(id => firestore.Document($"{PlayersPath}/{id}").GetSnapshotAsync(Source.Server)));
                foreach (var doc in docs)
                {
                    if (!doc.Exists) continue;
                    if (!LeaderboardEntry.TryParse(doc.Id, doc.ToDictionary(), out var entry))
                        throw new InvalidOperationException("Friend leaderboard data is invalid.");
                    players.Add(entry);
                }
            }
            return FriendsRanking.Build(uid, metric, ids, players);
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size) yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
