using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    public sealed partial class FirebaseLeaderboardService : ISocialService
    {
        public Task SetFollowingAsync(string target, bool following, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                ValidateSocialId(target);
                if (target == uid) throw new FriendRequestException("You cannot follow yourself.");
                var outgoing = firestore.Document($"social/{uid}/following/{target}");
                var incoming = firestore.Document($"social/{target}/followers/{uid}");
                await firestore.RunTransactionAsync(async transaction =>
                {
                    var existing = await transaction.GetSnapshotAsync(outgoing);
                    CheckAccount(uid, revision, work);
                    if (following)
                    {
                        if (existing.Exists) return;
                        var data = new Dictionary<string, object> { ["createdAt"] = FieldValue.ServerTimestamp };
                        transaction.Set(outgoing, data);
                        transaction.Set(incoming, data);
                    }
                    else { transaction.Delete(outgoing); transaction.Delete(incoming); }
                });
                return true;
            }, token);

        public Task SetActivitySharingAsync(bool sharing, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                CheckAccount(uid, revision, work);
                await firestore.Document($"socialProfiles/{uid}").SetAsync(new Dictionary<string, object> {
                    ["shareActivity"] = sharing, ["updatedAt"] = FieldValue.ServerTimestamp
                });
                CheckAccount(uid, revision, work);
                StepCountAndGpsManager.Instance?.GetComponent<FirebaseWalkBootstrap>()?.RequestRouteSharingSync();
                return true;
            }, token);

        public Task<SocialSnapshot> LoadSocialAsync(SocialTab tab, string target, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                // Register the same shareable code and photo used by the existing friends UI.
                await RegisterCodeAsync(uid, revision, work);
                string code = await GetFriendCodeAsync(work);
                CheckAccount(uid, revision, work);
                var following = await SocialLinksAsync(uid, "following", work);
                var followers = await SocialLinksAsync(uid, "followers", work);
                var me = await SocialPlayerAsync(uid, following, followers, work);
                var result = new SocialSnapshot { Me = me, Code = code,
                    FollowingCount = following.Count, FollowerCount = followers.Count };
                List<string> ids;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    var normalized = FriendCodes.Normalize(target);
                    if (normalized != null)
                    {
                        var lookup = await firestore.Document($"friendCodeLookup/{normalized}").GetSnapshotAsync(Source.Server);
                        if (!lookup.Exists) throw new FriendRequestException("Code not found. Ask them to open their Profile first.");
                        target = lookup.GetValue<string>("playerId");
                    }
                    ValidateSocialId(target);
                    ids = new List<string> { target };
                }
                else if (tab == SocialTab.Followers) ids = followers.ToList();
                else if (tab == SocialTab.Following) ids = following.ToList();
                else if (tab == SocialTab.Activity) ids = following.Concat(new[] { uid }).Distinct().ToList();
                else
                {
                    var found = await firestore.Collection(PlayersPath).OrderByDescending("totalDistanceMeters")
                        .Limit(50).GetSnapshotAsync(Source.Server);
                    ids = found.Documents.Select(d => d.Id).Where(id => id != uid).ToList();
                }
                foreach (var group in Chunk(ids, 10))
                {
                    CheckAccount(uid, revision, work);
                    result.Players.AddRange(await Task.WhenAll(group.Select(id => SocialPlayerAsync(id, following, followers, work))));
                }
                result.Players = result.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (tab == SocialTab.Profile) result.Profile = result.Players.Single();
                if (tab == SocialTab.Activity || tab == SocialTab.Profile)
                {
                    // Only query a collection that rules permit. Privacy is rechecked by the server.
                    var visible = result.Players.Where(p => p.Id == uid || (p.Following && p.SharesActivity)).ToList();
                    foreach (var group in Chunk(visible, 10))
                    {
                        CheckAccount(uid, revision, work);
                        var walks = await Task.WhenAll(group.Select(p => SocialWalksAsync(p, work)));
                        foreach (var list in walks) result.Activities.AddRange(list);
                    }
                    result.Activities = SocialFeed.Recent(result.Activities);
                }
                CheckAccount(uid, revision, work);
                return result;
            }, token);

        private async Task<HashSet<string>> SocialLinksAsync(string uid, string direction, CancellationToken token)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            DocumentSnapshot cursor = null;
            for (;;)
            {
                token.ThrowIfCancellationRequested();
                Query query = firestore.Collection($"social/{uid}/{direction}").OrderBy(FieldPath.DocumentId).Limit(50);
                if (cursor != null) query = query.StartAfter(cursor);
                var page = (await query.GetSnapshotAsync(Source.Server)).Documents.ToList();
                foreach (var doc in page) ids.Add(doc.Id);
                if (page.Count < 50) return ids;
                cursor = page[page.Count - 1];
            }
        }

        private async Task<SocialPlayer> SocialPlayerAsync(string id, HashSet<string> following,
            HashSet<string> followers, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var profile = await firestore.Document($"friendCodes/{id}").GetSnapshotAsync(Source.Server);
            var settings = await firestore.Document($"socialProfiles/{id}").GetSnapshotAsync(Source.Server);
            string name = FirebaseLeaderboardWriter.DefaultName(id);
            if (profile.Exists) name = profile.GetValue<string>("displayName");
            else
            {
                var publicProfile = await firestore.Document($"leaderboardProfiles/{id}").GetSnapshotAsync(Source.Server);
                var score = await firestore.Document($"{PlayersPath}/{id}").GetSnapshotAsync(Source.Server);
                if (!publicProfile.Exists && !score.Exists) throw new FriendRequestException("Player not found.");
                if (publicProfile.TryGetValue<string>("displayName", out var displayName)) name = displayName;
                else if (score.TryGetValue<string>("displayName", out displayName)) name = displayName;
            }
            token.ThrowIfCancellationRequested();
            return new SocialPlayer { Id = id, Name = name, Photo = ReadPhoto(profile),
                Following = following.Contains(id), FollowsYou = followers.Contains(id),
                SharesActivity = settings.Exists && settings.TryGetValue("shareActivity", out bool shareActivity) && shareActivity };
        }

        private async Task<List<SocialActivity>> SocialWalksAsync(SocialPlayer player, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var page = await firestore.Collection($"users/{player.Id}/walks").OrderByDescending("endedAtUtc")
                .Limit(10).GetSnapshotAsync(Source.Server);
            token.ThrowIfCancellationRequested();
            var activities = new List<SocialActivity>();
            foreach (var doc in page.Documents)
            {
                if (!doc.TryGetValue("endedAtUtc", out string endedStr) || !DateTime.TryParse(endedStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ended)) continue;
                
                double distance = 0, duration = 0;
                if (doc.TryGetValue("distanceMeters", out double dDouble)) distance = dDouble;
                else if (doc.TryGetValue("distanceMeters", out long dLong)) distance = dLong;
                
                if (doc.TryGetValue("durationSeconds", out double durDouble)) duration = durDouble;
                else if (doc.TryGetValue("durationSeconds", out long durLong)) duration = durLong;

                long steps = 0;
                if (doc.TryGetValue("steps", out long sLong)) steps = sLong;
                
                activities.Add(new SocialActivity { Id = doc.Id, Player = player, EndedUtc = ended,
                    Steps = steps, DistanceMeters = distance,
                    DurationSeconds = duration });
            }
            return activities;
        }

        private static void ValidateSocialId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Contains("/") || id.Any(char.IsWhiteSpace)
                || id == "." || id == "..") throw new FriendRequestException("Enter a valid player code.");
        }
    }
}
