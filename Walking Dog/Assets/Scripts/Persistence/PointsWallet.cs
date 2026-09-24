using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class PointsWalletSnapshot
{
    public const long Maximum = 9007199254740991;
    public long Balance { get; }
    public long TotalEarned { get; }
    public long TotalSpent { get; }
    public bool IsReconciling { get; }

    public PointsWalletSnapshot(long balance, long earned, long spent, bool reconciling = false)
    {
        if (balance < 0 || earned < 0 || spent < 0 || earned > Maximum || spent > earned || balance != earned - spent)
            throw new ArgumentException("Invalid points wallet.");
        Balance = balance; TotalEarned = earned; TotalSpent = spent; IsReconciling = reconciling;
    }

    public static long RewardForSteps(long steps)
    {
        if (steps < 0 || steps > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(steps));
        return steps / 10;
    }

    internal static PointsWalletSnapshot Parse(IDictionary<string, object> data, bool reconciling = false)
    {
        if (data == null || !data.TryGetValue("schemaVersion", out var version) || !(version is long v) || v != 1
            || !data.TryGetValue("balance", out var b) || !(b is long balance)
            || !data.TryGetValue("totalEarned", out var e) || !(e is long earned)
            || !data.TryGetValue("totalSpent", out var s) || !(s is long spent))
            throw new InvalidOperationException("Invalid points wallet.");
        return new PointsWalletSnapshot(balance, earned, spent, reconciling);
    }
}

internal interface IPointsWalletStore
{
    string AuthenticatedUserId { get; }
    void Reset();
    Task<PointsWalletSnapshot> LoadAsync(string owner, CancellationToken token);
}

// UI state is scoped to one account. Late/offline native calls cannot replace a
// different account's balance, and failed reads never masquerade as a zero balance.
public sealed class PointsWalletSession : IDisposable
{
    private readonly IPointsWalletStore store;
    private readonly TimeSpan timeout;
    private CancellationTokenSource pending;
    private int generation;
    private bool disposed;
    public string Owner { get; private set; } = "";
    public PointsWalletSnapshot Snapshot { get; private set; }
    public bool IsLoading { get; private set; }
    public bool NeedsSync { get; private set; }
    public bool HasPendingWalks { get; internal set; }

    internal PointsWalletSession(IPointsWalletStore store, TimeSpan? timeout = null)
    { this.store = store; this.timeout = timeout ?? TimeSpan.FromSeconds(25); SynchronizeAccount(); }

    public bool SynchronizeAccount()
    {
        if (disposed || Owner == (store.AuthenticatedUserId ?? "")) return false;
        Reset();
        return true;
    }

    internal void Reset()
    {
        if (disposed) return;
        generation++;
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        Owner = store.AuthenticatedUserId ?? "";
        store.Reset();
        Snapshot = null;
        HasPendingWalks = false;
        IsLoading = false;
        NeedsSync = !string.IsNullOrEmpty(Owner);
    }

    public string DisplayText
    {
        get
        {
            SynchronizeAccount();
            if (string.IsNullOrEmpty(Owner)) return "Sign in for points";
            if (Snapshot == null) return IsLoading ? "Loading points…" : "Points pending sync";
            return Snapshot.Balance.ToString("N0") + (NeedsSync || Snapshot.IsReconciling || HasPendingWalks ? " · sync pending" : "");
        }
    }

    public async Task RefreshAsync()
    {
        SynchronizeAccount();
        if (disposed || IsLoading || string.IsNullOrEmpty(Owner)) return;
        var revision = generation;
        var owner = Owner;
        pending?.Dispose();
        var request = pending = new CancellationTokenSource();
        var token = request.Token;
        IsLoading = true;
        try
        {
            var operation = store.LoadAsync(owner, token);
            if (await Task.WhenAny(operation, Task.Delay(timeout, token)) != operation)
            {
                Observe(operation);
                token.ThrowIfCancellationRequested();
                request.Cancel();
                throw new TimeoutException();
            }
            var snapshot = await operation;
            if (disposed || revision != generation || SynchronizeAccount() || token.IsCancellationRequested) return;
            Snapshot = snapshot;
            NeedsSync = snapshot.IsReconciling;
        }
        catch (Exception)
        {
            if (!disposed && revision == generation && !SynchronizeAccount()) NeedsSync = true;
        }
        finally
        {
            if (revision == generation && !disposed)
            {
                IsLoading = false;
                request.Cancel();
            }
        }
    }

    private static async void Observe(Task task) { try { await task; } catch (Exception) { } }
    public void Dispose()
    {
        disposed = true; generation++; pending?.Cancel(); pending?.Dispose(); pending = null;
        Owner = ""; Snapshot = null; IsLoading = false;
    }
}
