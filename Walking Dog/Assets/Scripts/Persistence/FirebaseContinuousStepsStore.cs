using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;
using WalkingDog.Leaderboards;

internal static class FirebaseContinuousStepsStore
{
    // Stream cursor, aggregate, wallet and leaderboard commit together. Absolute
    // cursors make offline retries and out-of-order acknowledgements harmless.
    internal static Task UploadAsync(FirebaseFirestore db, ContinuousSteps.Record record, CancellationToken token)
    {
        return FirebasePointsWalletStore.RetryContentionAsync(() => db.RunTransactionAsync(async tx =>
        {
            token.ThrowIfCancellationRequested();
            var uid = record.owner;
            var streamRef = db.Document($"users/{uid}/stepStreams/{record.streamId}");
            var aggregateRef = db.Document($"users/{uid}/activity/main");
            var walletRef = db.Document(FirebasePointsWalletStore.WalletPath(uid));
            var playerRef = db.Document($"{FirebaseLeaderboardWriter.Players}/{uid}");
            var stream = await tx.GetSnapshotAsync(streamRef);
            var aggregate = await tx.GetSnapshotAsync(aggregateRef);
            var wallet = await tx.GetSnapshotAsync(walletRef);
            var player = await tx.GetSnapshotAsync(playerRef);
            var profile = await tx.GetSnapshotAsync(db.Document($"leaderboardProfiles/{uid}"));
            var previous = stream.Exists ? stream.GetValue<long>("totalSteps") : 0;
            if (record.total <= previous) return;
            var delta = record.total - previous;
            var oldTotal = aggregate.Exists ? aggregate.GetValue<long>("totalSteps") : 0;
            var total = checked(oldTotal + delta);
            var points = total / 10 - oldTotal / 10;
            var oldWallet = wallet.Exists ? PointsWalletSnapshot.Parse(wallet.ToDictionary()) : new PointsWalletSnapshot(0, 0, 0);
            var earned = checked(oldWallet.TotalEarned + points);
            var steps = checked((player.Exists ? player.GetValue<long>("totalSteps") : 0) + delta);
            if (total > PointsWalletSnapshot.Maximum || earned > PointsWalletSnapshot.Maximum || steps > PointsWalletSnapshot.Maximum)
                throw new InvalidOperationException("Step or wallet limit reached.");
            token.ThrowIfCancellationRequested();
            tx.Set(streamRef, new Dictionary<string, object> { ["totalSteps"] = record.total, ["updatedAt"] = FieldValue.ServerTimestamp });
            tx.Set(aggregateRef, new Dictionary<string, object> { ["totalSteps"] = total,
                ["lastStreamId"] = record.streamId, ["updatedAt"] = FieldValue.ServerTimestamp });
            tx.Set(walletRef, FirebasePointsWalletStore.Fields(earned, oldWallet.TotalSpent, "activity"));
            tx.Set(playerRef, new Dictionary<string, object> {
                ["schemaVersion"] = 1,
                ["displayName"] = profile.Exists ? profile.GetValue<string>("displayName")
                    : player.Exists ? player.GetValue<string>("displayName") : FirebaseLeaderboardWriter.DefaultName(uid),
                ["totalSteps"] = steps,
                ["totalDistanceMeters"] = player.Exists ? player.GetValue<double>("totalDistanceMeters") : 0,
                ["completedWalkCount"] = player.Exists ? player.GetValue<long>("completedWalkCount") : 0,
                ["pointsBalance"] = earned - oldWallet.TotalSpent,
                ["lastWalkId"] = "activity", ["updatedAt"] = FieldValue.ServerTimestamp
            });
        }), token);
    }
}
