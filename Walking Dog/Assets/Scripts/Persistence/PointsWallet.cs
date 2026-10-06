using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class PointsWalletSnapshot
{
    public const long Maximum = 9007199254740991;
    // Change this one value to tune free rolls. Set it to 10 only for a notification test,
    // then restore 10000 before shipping.
    public const long StepsPerMilestoneRoll = 10000;
    public long Balance { get; }
    public long TotalEarned { get; }
    public long TotalSpent { get; }
    public long MilestoneRollsClaimed { get; }
    public bool IsReconciling { get; }

    public PointsWalletSnapshot(long balance, long earned, long spent, long milestoneRollsClaimed = 0, bool reconciling = false)
    {
        if (balance < 0 || earned < 0 || spent < 0 || earned > Maximum || spent > earned || balance != earned - spent)
            throw new ArgumentException("Invalid points wallet.");
            
        long maxRolls = MilestoneRollsForEarnedPoints(earned);
        if (milestoneRollsClaimed < 0) milestoneRollsClaimed = 0;
        if (milestoneRollsClaimed > maxRolls) milestoneRollsClaimed = maxRolls;
        
        Balance = balance; TotalEarned = earned; TotalSpent = spent; MilestoneRollsClaimed = milestoneRollsClaimed; IsReconciling = reconciling;
    }

    public static long RewardForSteps(long steps)
    {
        if (steps < 0 || steps > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(steps));
        return steps / 10;
    }

    /// <summary>Calculates the number of free rolls earned from physical steps.</summary>
    public static long MilestoneRollsForSteps(long steps)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        return steps / StepsPerMilestoneRoll;
    }

    /// <summary>Calculates free rolls from wallet points, where one point represents ten steps.</summary>
    public static long MilestoneRollsForEarnedPoints(long earned)
    {
        if (earned < 0) throw new ArgumentOutOfRangeException(nameof(earned));
        return checked(earned * 10) / StepsPerMilestoneRoll;
    }

    private static long ParseLong(object obj)
    {
        if (obj == null) return 0;
        if (obj is long l) return l;
        if (obj is int i) return i;
        if (obj is double d) return (long)d;
        if (obj is float f) return (long)f;
        if (obj is string s && long.TryParse(s, out var p)) return p;
        throw new InvalidCastException();
    }

    internal static PointsWalletSnapshot Parse(IDictionary<string, object> data, bool reconciling = false)
    {
        if (data == null) throw new InvalidOperationException("Wallet data is null.");
        
        try 
        {
            long v = data.TryGetValue("schemaVersion", out var versionObj) ? ParseLong(versionObj) : 0;
            if (v != 1) throw new InvalidOperationException("Unsupported schema version.");

            long balance = data.TryGetValue("balance", out var bObj) ? ParseLong(bObj) : 0;
            long earned = data.TryGetValue("totalEarned", out var eObj) ? ParseLong(eObj) : 0;
            long spent = data.TryGetValue("totalSpent", out var sObj) ? ParseLong(sObj) : 0;
            long milestoneRollsClaimed = data.TryGetValue("milestoneRollsClaimed", out var mObj) ? ParseLong(mObj) : 0;

            return new PointsWalletSnapshot(balance, earned, spent, milestoneRollsClaimed, reconciling);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid points wallet format.", ex);
        }
    }
}

internal interface IPointsWalletStore
{
    string AuthenticatedUserId { get; }
    void Reset();
    Task<PointsWalletSnapshot> LoadAsync(string owner, CancellationToken token);
    Task SpendAsync(string owner, long amount, string receiptId, CancellationToken token);
    Task ClaimMilestoneRollAsync(string owner, string receiptId, CancellationToken token);
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

    public async Task SpendPointsAsync(long amount, string receiptId)
    {
        SynchronizeAccount();
        if (string.IsNullOrEmpty(Owner)) throw new InvalidOperationException("Not authenticated");
        if (Snapshot == null || Snapshot.Balance < amount) throw new InvalidOperationException("Insufficient points");
        
        using (var tokenSource = new CancellationTokenSource(timeout))
        {
            await store.SpendAsync(Owner, amount, receiptId, tokenSource.Token);
        }
        // Force refresh after spend
        await RefreshAsync();
    }

    public async Task ClaimMilestoneRollAsync(string receiptId)
    {
        SynchronizeAccount();
        if (string.IsNullOrEmpty(Owner)) throw new InvalidOperationException("Not authenticated");
        if (Snapshot == null) throw new InvalidOperationException("Wallet not ready");
        
        long totalFreeRolls = PointsWalletSnapshot.MilestoneRollsForEarnedPoints(Snapshot.TotalEarned);
        if (totalFreeRolls <= Snapshot.MilestoneRollsClaimed) throw new InvalidOperationException("No milestone rolls available.");

        using (var tokenSource = new CancellationTokenSource(timeout))
        {
            await store.ClaimMilestoneRollAsync(Owner, receiptId, tokenSource.Token);
        }
        await RefreshAsync();
    }

    private static async void Observe(Task task) { try { await task; } catch (Exception) { } }
    public void Dispose()
    {
        disposed = true; generation++; pending?.Cancel(); pending?.Dispose(); pending = null;
        Owner = ""; Snapshot = null; IsLoading = false;
    }
}
