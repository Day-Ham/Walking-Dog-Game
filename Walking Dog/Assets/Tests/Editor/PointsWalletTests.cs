using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

public sealed class PointsWalletTests
{
    private sealed class Store : IPointsWalletStore
    {
        public string AuthenticatedUserId { get; set; } = "alice";
        public int Resets;
        public Func<string, CancellationToken, Task<PointsWalletSnapshot>> Read = (_, __) => Task.FromResult(new PointsWalletSnapshot(12, 12, 0));
        public void Reset() { Resets++; }
        public Task<PointsWalletSnapshot> LoadAsync(string owner, CancellationToken token) => Read(owner, token);
        public Task SpendAsync(string owner, long amount, string receiptId, CancellationToken token) => Task.CompletedTask;
        public Task ClaimMilestoneRollAsync(string owner, string receiptId, CancellationToken token) => Task.CompletedTask;
    }

    [TestCase(0, 0)] [TestCase(9, 0)] [TestCase(10, 1)] [TestCase(129, 12)] [TestCase(int.MaxValue, 214748364)]
    public void RewardsUseIntegerStepsAndRoundDownPerWalk(long steps, long expected)
        => Assert.That(PointsWalletSnapshot.RewardForSteps(steps), Is.EqualTo(expected));

    [Test] public void InvalidWalletsAndStepCountsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PointsWalletSnapshot.RewardForSteps(-1));
        Assert.Throws<ArgumentException>(() => new PointsWalletSnapshot(10, 20, 0));
        Assert.Throws<ArgumentException>(() => new PointsWalletSnapshot(0, -1, -1));
        Assert.Throws<InvalidOperationException>(() => PointsWalletSnapshot.Parse(new Dictionary<string, object> {
            ["schemaVersion"] = 1L, ["balance"] = true, ["totalEarned"] = 12L, ["totalSpent"] = 0L
        }));
    }

    [Test] public void MilestoneCountsAllowLegacyWalletsAndRejectMalformedOrUnearnedClaims()
    {
        var data = new Dictionary<string, object> {
            ["schemaVersion"] = 1L, ["balance"] = 2000L, ["totalEarned"] = 2000L, ["totalSpent"] = 0L
        };
        Assert.That(PointsWalletSnapshot.Parse(data).MilestoneRollsClaimed, Is.Zero);
        data["milestoneRollsClaimed"] = 2L;
        Assert.That(PointsWalletSnapshot.Parse(data).MilestoneRollsClaimed, Is.EqualTo(2));
        data["milestoneRollsClaimed"] = 3L;
        Assert.Throws<ArgumentException>(() => PointsWalletSnapshot.Parse(data));
        data["milestoneRollsClaimed"] = -1L;
        Assert.Throws<ArgumentException>(() => PointsWalletSnapshot.Parse(data));
        data["milestoneRollsClaimed"] = 1.0;
        Assert.Throws<InvalidOperationException>(() => PointsWalletSnapshot.Parse(data));
    }

    [Test] public void MissingNullAndNonintegerWalletFieldsCannotCreateABalance()
    {
        var fields = new[] { "schemaVersion", "balance", "totalEarned", "totalSpent" };
        foreach (var field in fields)
        {
            var data = new Dictionary<string, object> {
                ["schemaVersion"] = 1L, ["balance"] = 0L, ["totalEarned"] = 0L, ["totalSpent"] = 0L
            };
            data.Remove(field);
            Assert.Throws<InvalidOperationException>(() => PointsWalletSnapshot.Parse(data), "Missing " + field);
            foreach (var malformed in new object[] { null, 0.5, 0.0, "0", true, double.NaN, double.PositiveInfinity })
            {
                data[field] = malformed;
                Assert.Throws<InvalidOperationException>(() => PointsWalletSnapshot.Parse(data), "Malformed " + field);
            }
        }
    }

    [Test] public async Task LoadsSavedBalanceAndKeepsItOnOfflineFailure()
    {
        var store = new Store();
        using (var session = new PointsWalletSession(store))
        {
            await session.RefreshAsync();
            Assert.That(session.Snapshot.Balance, Is.EqualTo(12));
            Assert.That(session.DisplayText, Is.EqualTo("12"));
            store.Read = (_, __) => Task.FromException<PointsWalletSnapshot>(new Exception("offline"));
            await session.RefreshAsync();
            Assert.That(session.Snapshot.Balance, Is.EqualTo(12));
            Assert.That(session.DisplayText, Does.Contain("sync pending"));
        }
    }

    [Test] public async Task ChangedAccountsClearBalanceAndRejectLateResults()
    {
        var store = new Store();
        using (var session = new PointsWalletSession(store))
        {
            await session.RefreshAsync();
            var delayed = new TaskCompletionSource<PointsWalletSnapshot>();
            store.Read = (_, __) => delayed.Task;
            var old = session.RefreshAsync();
            store.AuthenticatedUserId = "bob";
            Assert.That(session.SynchronizeAccount(), Is.True);
            Assert.That(session.Snapshot, Is.Null);
            store.Read = (_, __) => Task.FromResult(new PointsWalletSnapshot(5, 5, 0));
            await session.RefreshAsync();
            delayed.SetResult(new PointsWalletSnapshot(999, 999, 0));
            await old;
            Assert.That(session.Snapshot.Balance, Is.EqualTo(5));
            store.AuthenticatedUserId = "";
            Assert.That(session.DisplayText, Is.EqualTo("Sign in for points"));
            Assert.That(session.Snapshot, Is.Null);
            Assert.That(store.Resets, Is.EqualTo(3));
        }
    }

    [Test] public async Task TimeoutDoesNotInventZeroAndCanRetry()
    {
        var store = new Store();
        var delayed = new TaskCompletionSource<PointsWalletSnapshot>();
        store.Read = (_, __) => delayed.Task;
        using (var session = new PointsWalletSession(store, TimeSpan.FromMilliseconds(10)))
        {
            await session.RefreshAsync();
            Assert.That(session.Snapshot, Is.Null);
            Assert.That(session.DisplayText, Is.EqualTo("Points pending sync"));
            delayed.SetResult(new PointsWalletSnapshot(999, 999, 0));
            Assert.That(session.Snapshot, Is.Null);
            store.Read = (_, __) => Task.FromResult(new PointsWalletSnapshot(0, 0, 0));
            await session.RefreshAsync();
            Assert.That(session.DisplayText, Is.EqualTo("0"));
        }
    }

    [Test] public async Task ResetRejectsLateResultsEvenWhenSameAccountSignsBackIn()
    {
        var store = new Store();
        using (var session = new PointsWalletSession(store))
        {
            var delayed = new TaskCompletionSource<PointsWalletSnapshot>();
            store.Read = (_, __) => delayed.Task;
            var old = session.RefreshAsync();
            session.Reset();
            delayed.SetResult(new PointsWalletSnapshot(999, 999, 0));
            await old;
            Assert.That(session.Snapshot, Is.Null);
            Assert.That(session.IsLoading, Is.False);
        }
    }

    [Test] public async Task HistoryReconciliationAndLocalUploadsRemainVisible()
    {
        var store = new Store { Read = (_, __) => Task.FromResult(new PointsWalletSnapshot(12, 12, 0, 0, true)) };
        using (var session = new PointsWalletSession(store))
        {
            await session.RefreshAsync();
            Assert.That(session.DisplayText, Does.Contain("sync pending"));
            store.Read = (_, __) => Task.FromResult(new PointsWalletSnapshot(20, 20, 0));
            await session.RefreshAsync();
            session.HasPendingWalks = true;
            Assert.That(session.DisplayText, Does.Contain("sync pending"));
            session.HasPendingWalks = false;
            Assert.That(session.DisplayText, Is.EqualTo("20"));
        }
    }
}
