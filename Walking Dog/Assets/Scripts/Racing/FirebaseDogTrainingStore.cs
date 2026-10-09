using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;

namespace WalkingDog.Racing
{
    internal interface IDogTrainingStore
    {
        Task<DogStats> LoadAsync(string owner, CancellationToken token);
        Task<DogStats> TrainAsync(string owner, DogStat stat, string receiptId, CancellationToken token);
    }

    internal sealed class FirebaseDogTrainingStore : IDogTrainingStore
    {
        private readonly FirebaseAuth auth;
        private readonly FirebaseFirestore db;
        internal FirebaseDogTrainingStore(FirebaseAuth auth, FirebaseFirestore db) { this.auth = auth; this.db = db; }
        private void Check(string owner, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(owner) || auth.CurrentUser?.UserId != owner) throw new OperationCanceledException();
        }

        internal static Dictionary<string, object> Fields(DogStats stats, string receiptId) => new Dictionary<string, object> {
            ["schemaVersion"] = 1, ["speed"] = stats.speed, ["stamina"] = stats.stamina,
            ["acceleration"] = stats.acceleration, ["trainingCount"] = stats.trainingCount,
            ["lastTrainingId"] = receiptId, ["updatedAt"] = FieldValue.ServerTimestamp
        };

        public async Task<DogStats> LoadAsync(string owner, CancellationToken token)
        {
            Check(owner, token);
            return await db.RunTransactionAsync(async tx => {
                var reference = db.Document($"users/{owner}/dog/main");
                var saved = await tx.GetSnapshotAsync(reference);
                Check(owner, token);
                if (saved.Exists) return DogStats.Parse(saved.ToDictionary());
                var baseline = new DogStats();
                tx.Set(reference, Fields(baseline, ""));
                return baseline;
            });
        }

        public async Task<DogStats> TrainAsync(string owner, DogStat stat, string receiptId, CancellationToken token)
        {
            Check(owner, token);
            if (!Guid.TryParseExact(receiptId, "N", out _)) throw new ArgumentException("Invalid training receipt.");
            return await RetryAsync(() => db.RunTransactionAsync(async tx => {
                var receiptRef = db.Document($"users/{owner}/dogTrainingReceipts/{receiptId}");
                var dogRef = db.Document($"users/{owner}/dog/main");
                var walletRef = db.Document(FirebasePointsWalletStore.WalletPath(owner));
                var receipt = await tx.GetSnapshotAsync(receiptRef);
                var dog = await tx.GetSnapshotAsync(dogRef);
                var wallet = await tx.GetSnapshotAsync(walletRef);
                Check(owner, token);
                if (!dog.Exists || !wallet.Exists) throw new InvalidOperationException("Load your dog and wallet before training.");
                var stats = DogStats.Parse(dog.ToDictionary());
                if (receipt.Exists) return stats; // An acknowledged retry cannot charge twice.
                var old = PointsWalletSnapshot.Parse(wallet.ToDictionary());
                var next = stats.Train(stat, old.Balance);
                var lastWalkId = wallet.GetValue<string>("lastWalkId");
                Check(owner, token);
                tx.Set(walletRef, FirebasePointsWalletStore.Fields(old.TotalEarned,
                    checked(old.TotalSpent + DogStats.TrainingCost), lastWalkId, old.MilestoneRollsClaimed));
                tx.Set(dogRef, Fields(next, receiptId));
                tx.Set(receiptRef, new Dictionary<string, object> {
                    ["stat"] = DogStats.Key(stat), ["cost"] = DogStats.TrainingCost,
                    ["trainedAt"] = FieldValue.ServerTimestamp
                });
                return next;
            }), token);
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return await operation(); }
                catch (Firebase.FirebaseException error) when (error.ErrorCode == (int)FirestoreError.PermissionDenied && attempt < 2)
                { await Task.Delay(100 * (attempt + 1), token); }
            }
        }
    }
}
