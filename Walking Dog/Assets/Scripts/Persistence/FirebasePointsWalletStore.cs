using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;

internal sealed class FirebasePointsWalletStore : IPointsWalletStore
{
    private readonly FirebaseAuth auth;
    private readonly FirebaseFirestore db;
    private string scanOwner;
    private Scan scan;
    private sealed class Scan { public DocumentSnapshot Cursor; public bool Complete; }
    public string AuthenticatedUserId => auth.CurrentUser?.UserId ?? "";
    internal FirebasePointsWalletStore(FirebaseAuth auth, FirebaseFirestore db) { this.auth = auth; this.db = db; }
    public void Reset() { scanOwner = null; scan = null; }
    internal static string WalletPath(string uid) => $"users/{uid}/wallet/main";
    internal static string ReceiptPath(string uid, string id) => $"users/{uid}/pointReceipts/{id}";

    internal static Dictionary<string, object> Fields(long earned, long spent, string walkId) => new Dictionary<string, object>
    {
        ["schemaVersion"] = 1, ["balance"] = earned - spent, ["totalEarned"] = earned,
        ["totalSpent"] = spent, ["lastWalkId"] = walkId, ["updatedAt"] = FieldValue.ServerTimestamp
    };

    // Rules require this exact saved-walk delta and its immutable receipt together.
    // No balance setter or spending permission is exposed to the client.
    internal static Task AwardAsync(FirebaseFirestore db, string uid, string walkId,
        CancellationToken token = default)
    {
        return RetryContentionAsync(() => db.RunTransactionAsync(async transaction =>
        {
            token.ThrowIfCancellationRequested();
            var receiptRef = db.Document(ReceiptPath(uid, walkId));
            var receipt = await transaction.GetSnapshotAsync(receiptRef);
            if (receipt.Exists) return;
            var walk = await transaction.GetSnapshotAsync(db.Document($"users/{uid}/walks/{walkId}"));
            if (!walk.Exists) throw new InvalidOperationException("Save the walk before awarding points.");
            var points = PointsWalletSnapshot.RewardForSteps(walk.GetValue<long>("steps"));
            var walletRef = db.Document(WalletPath(uid));
            var wallet = await transaction.GetSnapshotAsync(walletRef);
            var old = wallet.Exists ? PointsWalletSnapshot.Parse(wallet.ToDictionary()) : new PointsWalletSnapshot(0, 0, 0);
            var earned = checked(old.TotalEarned + points);
            if (earned > PointsWalletSnapshot.Maximum) throw new InvalidOperationException("Wallet limit reached.");
            token.ThrowIfCancellationRequested();
            transaction.Set(walletRef, Fields(earned, old.TotalSpent, walkId));
            transaction.Set(receiptRef, new Dictionary<string, object> {
                ["points"] = points, ["awardedAt"] = FieldValue.ServerTimestamp
            });
        }), token);
    }

    // A concurrent client commit can make an exact-delta rules check fail before
    // the SDK reports a transaction conflict. Retry from fresh reads; persistent
    // denials still surface to the durable sync queue. Rules are never relaxed.
    private static async Task RetryContentionAsync(Func<Task> operation, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { await operation(); return; }
            catch (Firebase.FirebaseException error) when (error.ErrorCode == (int)FirestoreError.PermissionDenied && attempt < 2)
            { await Task.Delay(100 * (attempt + 1), token); }
        }
    }

    public async Task<PointsWalletSnapshot> LoadAsync(string owner, CancellationToken token)
    {
        Check(owner, token);
        if (scanOwner != owner) { scanOwner = owner; scan = new Scan(); }
        var current = scan;
        var walletRef = db.Document(WalletPath(owner));
        // A zero wallet is useful even before the account's first completed walk.
        await RetryContentionAsync(() => db.RunTransactionAsync(async transaction =>
        {
            Check(owner, token);
            var wallet = await transaction.GetSnapshotAsync(walletRef);
            Check(owner, token);
            if (!wallet.Exists) transaction.Set(walletRef, Fields(0, 0, ""));
        }), token);
        Check(owner, token);
        if (!current.Complete)
        {
            Query query = db.Collection($"users/{owner}/walks").OrderBy(FieldPath.DocumentId).Limit(25);
            if (current.Cursor != null) query = query.StartAfter(current.Cursor);
            var page = await query.GetSnapshotAsync(Source.Server);
            Check(owner, token);
            int count = 0;
            foreach (var walk in page.Documents)
            {
                Check(owner, token);
                await AwardAsync(db, owner, walk.Id, token);
                Check(owner, token);
                current.Cursor = walk;
                count++;
            }
            current.Complete = count < 25;
        }
        var saved = await walletRef.GetSnapshotAsync(Source.Server);
        Check(owner, token);
        return PointsWalletSnapshot.Parse(saved.ToDictionary(), !current.Complete);
    }

    public Task SpendAsync(string owner, long amount, string receiptId, CancellationToken token)
    {
        Check(owner, token);
        return RetryContentionAsync(() => db.RunTransactionAsync(async transaction =>
        {
            token.ThrowIfCancellationRequested();
            var receiptRef = db.Document($"users/{owner}/spendReceipts/{receiptId}");
            var receipt = await transaction.GetSnapshotAsync(receiptRef);
            if (receipt.Exists) return;

            var walletRef = db.Document(WalletPath(owner));
            var wallet = await transaction.GetSnapshotAsync(walletRef);
            if (!wallet.Exists) throw new InvalidOperationException("Wallet does not exist.");
            var dict = wallet.ToDictionary();
            var old = PointsWalletSnapshot.Parse(dict);
            
            if (old.Balance < amount) throw new InvalidOperationException("Not enough points.");
            
            var spent = checked(old.TotalSpent + amount);
            
            dict.TryGetValue("lastWalkId", out var walkIdObj);
            string lastWalkId = walkIdObj as string ?? "";

            transaction.Set(walletRef, Fields(old.TotalEarned, spent, lastWalkId));
            transaction.Set(receiptRef, new Dictionary<string, object> {
                ["points"] = amount, ["spentAt"] = FieldValue.ServerTimestamp
            });
        }), token);
    }

    private void Check(string owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(owner) || owner != AuthenticatedUserId) throw new OperationCanceledException();
    }
}
