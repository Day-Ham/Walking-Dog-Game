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
                        result.Add(new FriendEntry(link.Id, name, link.GetValue<string>("requestedBy"), link.GetValue<string>("status") == "accepted"));
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
                if (uid == friendCode) throw new FriendRequestException("That is your own friend code.");
                await RegisterCodeAsync(uid, revision, work);
                CheckAccount(uid, revision, work);
                var own = firestore.Document($"friends/{uid}/links/{friendCode}");
                var other = firestore.Document($"friends/{friendCode}/links/{uid}");
                await firestore.RunTransactionAsync(async transaction =>
                {
                    var link = await transaction.GetSnapshotAsync(own);
                    var target = await transaction.GetSnapshotAsync(firestore.Document($"friendCodes/{friendCode}"));
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
                if (!existing.Exists || existing.GetValue<string>("displayName") != name)
                    transaction.Set(codeRef, new Dictionary<string, object> { ["displayName"] = name, ["updatedAt"] = FieldValue.ServerTimestamp });
            });

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
