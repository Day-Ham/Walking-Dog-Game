using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    public sealed partial class FirebaseLeaderboardService : ISocialRouteService
    {
        public Task<SocialRoute> LoadRouteAsync(string owner, string walkId, CancellationToken token)
            => FriendOperationAsync(async (uid, revision, work) =>
            {
                ValidateSocialId(owner); ValidateSocialId(walkId);
                var doc = await firestore.Document($"users/{owner}/sharedRoutes/{walkId}").GetSnapshotAsync(Source.Server);
                CheckAccount(uid, revision, work);
                var result = new SocialRoute { IsOwner = owner == uid, IsShared = doc.Exists };
                if (result.IsOwner)
                {
                    result.Points = SocialRouteCodec.OwnedLocalRoute(StepCountAndGpsManager.Instance?.LocalWalks, uid, walkId);
                    var settings = await firestore.Document($"socialProfiles/{uid}").GetSnapshotAsync(Source.Server);
                    CheckAccount(uid, revision, work);
                    result.CanShare = settings.Exists && settings.GetValue<bool>("shareActivity") && result.Points.Count > 0;
                }
                if (result.Points.Count == 0 && doc.Exists)
                    result.Points = SocialRouteCodec.Decode(doc.GetValue<string>("routeJson"));
                result.Message = result.IsOwner
                    ? result.IsShared ? "This route is shared with your followers."
                    : result.Points.Count == 0 ? "No GPS route is saved on this device. Open this walk on the phone that recorded it."
                    : result.CanShare ? "Only you can see this route until you share it with followers."
                    : "Your route is private. Turn Walk sharing ON to share this route."
                    : result.Points.Count == 0 ? "The walker hasn't shared this session's GPS route yet." : "Shared route · Gaps indicate missing GPS recordings.";
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
                    var local = SocialRouteCodec.OwnedLocalRoute(StepCountAndGpsManager.Instance?.LocalWalks, uid, walkId);
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
