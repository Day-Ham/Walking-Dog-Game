using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Leaderboards
{
    // No privileged credentials or Cloud Functions. Firestore rules verify the
    // exact saved-walk delta and require receipt + score to commit together.
    internal static class FirebaseLeaderboardWriter
    {
        internal const string Players = "leaderboards/allTime/players";

        //points
        internal const string PointsBalanceField = "pointsBalance";

        internal sealed class ReconciliationProgress
        {
            public DocumentSnapshot Cursor;
            public bool Complete;
        }

        public static Task CountWalkAsync(FirebaseFirestore db, string uid, string walkId)
        {
            var receiptRef = db.Document($"leaderboardReceipts/{uid}/walks/{walkId}");
            var playerRef = db.Document($"{Players}/{uid}");
            return db.RunTransactionAsync(async transaction =>
            {
                var receipt = await transaction.GetSnapshotAsync(receiptRef);
                var player = await transaction.GetSnapshotAsync(playerRef);


                // Firebase wallet balance is the source of truth. Copy it into the
                // public leaderboard record; do not recalculate it from steps.

                var wallet = await transaction.GetSnapshotAsync(db.Document($"users/{uid}/wallet/main"));
                var pointsBalance = wallet.Exists ? wallet.GetValue<long>("balance") : 0;
               
                
                // Reconciliation revisits already-counted walks. Use that pass to
                // refresh this player's balance after a future currency purchase.
                if (receipt.Exists)
                {
                    if (player.Exists) transaction.Update(playerRef, new Dictionary<string, object> {
                        [PointsBalanceField] = pointsBalance, ["updatedAt"] = FieldValue.ServerTimestamp
                    });
                    return;
                }
                var walk = await transaction.GetSnapshotAsync(db.Document($"users/{uid}/walks/{walkId}"));
                if (!walk.Exists) return;
                var steps = walk.GetValue<long>("steps");
                var distance = walk.GetValue<double>("distanceMeters");
                if (steps == 0 && distance == 0) return;
                var profile = await transaction.GetSnapshotAsync(db.Document($"leaderboardProfiles/{uid}"));
                var totalSteps = checked((player.Exists ? player.GetValue<long>("totalSteps") : 0) + steps);
                var totalDistance = (player.Exists ? player.GetValue<double>("totalDistanceMeters") : 0) + distance;
                var count = checked((player.Exists ? player.GetValue<long>("completedWalkCount") : 0) + 1);
                var name = profile.Exists ? profile.GetValue<string>("displayName")
                    : player.Exists ? player.GetValue<string>("displayName") : DefaultName(uid);
                transaction.Set(playerRef, new Dictionary<string, object> {
                    ["schemaVersion"] = 1, ["displayName"] = name,
                    ["totalSteps"] = totalSteps, ["totalDistanceMeters"] = totalDistance,
                    ["completedWalkCount"] = count, [PointsBalanceField] = pointsBalance,
                    ["lastWalkId"] = walkId,
                    ["updatedAt"] = FieldValue.ServerTimestamp
                });
                transaction.Set(receiptRef, new Dictionary<string, object> { ["countedAt"] = FieldValue.ServerTimestamp });
            });
        }

        // Call this after any future wallet spending transaction succeeds. It keeps
        // the leaderboard card bound to the Firebase currency balance
        // important to use this for gacha implementation
        internal static Task SynchronizePointsBalanceAsync(FirebaseFirestore db, string uid)
        {
            var playerRef = db.Document($"{Players}/{uid}");
            var walletRef = db.Document($"users/{uid}/wallet/main");
            return db.RunTransactionAsync(async transaction =>
            {
                var player = await transaction.GetSnapshotAsync(playerRef);
                var wallet = await transaction.GetSnapshotAsync(walletRef);
                if (!player.Exists) return;
                var balance = wallet.Exists ? wallet.GetValue<long>("balance") : 0;
                transaction.Update(playerRef, new Dictionary<string, object> {
                    [PointsBalanceField] = balance, ["updatedAt"] = FieldValue.ServerTimestamp
                });
            });
        }

        public static Task SaveNameAsync(FirebaseFirestore db, string uid, string name)
        {
            var playerRef = db.Document($"{Players}/{uid}");
            return db.RunTransactionAsync(async transaction =>
            {
                var player = await transaction.GetSnapshotAsync(playerRef);
                transaction.Set(db.Document($"leaderboardProfiles/{uid}"), new Dictionary<string, object> {
                    ["displayName"] = name, ["updatedAt"] = FieldValue.ServerTimestamp
                });
                transaction.Set(db.Document($"friendCodes/{uid}"), new Dictionary<string, object> {
                    ["displayName"] = name, ["updatedAt"] = FieldValue.ServerTimestamp
                }, SetOptions.MergeAll);
                if (player.Exists) transaction.Update(playerRef, new Dictionary<string, object> {
                    ["displayName"] = name, ["updatedAt"] = FieldValue.ServerTimestamp
                });
            });
        }

        // Recover old summaries and interrupted updates. Page in bounded reads;
        // receipts skip already-counted walks. Cancellation stops further pages.
        public static async Task ReconcileAsync(FirebaseFirestore db, string uid, ReconciliationProgress progress, CancellationToken token)
        {
            if (progress.Complete) return;
            for (;;)
            {
                token.ThrowIfCancellationRequested();
                Query query = db.Collection($"users/{uid}/walks").OrderByDescending("startedAtUtc").Limit(50);
                if (progress.Cursor != null) query = query.StartAfter(progress.Cursor);
                var page = await query.GetSnapshotAsync(Source.Server);
                int count = 0;
                foreach (var walk in page.Documents)
                {
                    token.ThrowIfCancellationRequested();
                    await CountWalkAsync(db, uid, walk.Id);
                    progress.Cursor = walk;
                    count++;
                }
                if (count < 50) { progress.Complete = true; return; }
            }
        }

        internal static string DefaultName(string uid)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(uid));
                return "Walker-" + BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
