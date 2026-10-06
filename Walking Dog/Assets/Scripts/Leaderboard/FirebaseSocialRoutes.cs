using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    public sealed partial class FirebaseLeaderboardService : ISocialRouteService
    {
        internal Func<string, string, List<StepCountAndGpsManager.WalkRoutePoint>> LocalRouteReader;
        internal Func<string, string, CancellationToken, Task<SocialRoute>> SharedRouteReader;

        public async Task<SocialRoute> LoadRouteAsync(string owner, string walkId, CancellationToken token)
        {
            var uid = RequireUser(token);
            var revision = authRevision;
            ValidateSocialId(owner); ValidateSocialId(walkId);
            var local = owner == uid ? ReadLocalRoute(owner, walkId) : new List<StepCountAndGpsManager.WalkRoutePoint>();
            SocialRoute result;
            try
            {
                result = await (SharedRouteReader != null ? SharedRouteReader(owner, walkId, token) : LoadSharedRouteAsync(owner, walkId, token));
                CheckAccount(uid, revision, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (owner == uid && local.Count > 0)
            {
                CheckAccount(uid, revision, token);
                // Local GPS remains usable if cloud permissions/network fail.
                // Sharing state is unknown, so no sharing mutation is offered.
                return new SocialRoute { IsOwner = true, Points = local,
                    Message = "Showing the route saved on this device. Cloud sharing couldn't be checked; reopen to retry." };
            }
            if (local.Count > 0) result.Points = local;
            result.CanShare = result.CanShare && result.Points.Count > 0;
            result.Message = result.IsOwner
                ? result.IsShared ? "Followers can see this route while Walk sharing is ON."
                : result.Points.Count == 0 ? "No GPS route is saved on this device or in the cloud. Open this walk on the phone that recorded it."
                : result.CanShare ? "Walk sharing is ON. This route will share automatically after it syncs; keep this phone online."
                : "Your route is private. Turn Walk sharing ON to share this route."
                : result.Points.Count == 0 ? "This walk's route hasn't synced yet. The recording phone shares it automatically while Walk sharing is ON." : "Shared route · Gaps indicate missing GPS recordings.";
            return result;
        }

        private List<StepCountAndGpsManager.WalkRoutePoint> ReadLocalRoute(string owner, string walkId)
            => LocalRouteReader != null ? LocalRouteReader(owner, walkId)
                : SocialRouteCodec.OwnedLocalRoute(StepCountAndGpsManager.Instance?.LocalWalks, owner, walkId);

        private Task<SocialRoute> LoadSharedRouteAsync(string owner, string walkId, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                ValidateSocialId(owner); ValidateSocialId(walkId);
                var doc = await firestore.Document($"users/{owner}/sharedRoutes/{walkId}").GetSnapshotAsync(Source.Server);
                CheckAccount(uid, revision, work);
                var result = new SocialRoute { IsOwner = owner == uid, IsShared = doc.Exists };
                if (result.IsOwner)
                {
                    var settings = await firestore.Document($"socialProfiles/{uid}").GetSnapshotAsync(Source.Server);
                    CheckAccount(uid, revision, work);
                    result.CanShare = settings.Exists && settings.TryGetValue("shareActivity", out bool shareActivity) && shareActivity;
                }
                if (doc.Exists)
                    result.Points = SocialRouteCodec.Decode(doc.GetValue<string>("routeJson"));
                return result;
            }, token);

        public Task SetRouteSharedAsync(string owner, string walkId, bool shared, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                ValidateSocialId(owner); ValidateSocialId(walkId);
                if (owner != uid) throw new InvalidOperationException("Only the walker can share this route.");
                var reference = firestore.Document($"users/{uid}/sharedRoutes/{walkId}");
                if (!shared)
                {
                    CheckAccount(uid, revision, work);
                    await reference.DeleteAsync();
                }
                else
                {
                    var local = ReadLocalRoute(uid, walkId);
                    var json = SocialRouteCodec.Encode(local);
                    CheckAccount(uid, revision, work);
                    await reference.SetAsync(new Dictionary<string, object> {
                        ["schemaVersion"] = 1, ["walkId"] = walkId, ["routeJson"] = json, ["updatedAt"] = FieldValue.ServerTimestamp
                    });
                }
                return true;
            }, token);
    }
}
