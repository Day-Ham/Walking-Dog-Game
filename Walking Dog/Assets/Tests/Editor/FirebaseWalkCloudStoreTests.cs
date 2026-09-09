using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

public sealed class FirebaseWalkCloudStoreTests
{
    [Test]
    public async Task UploadWaitsForAcknowledgementAndRetriesUseSamePath()
    {
        var acknowledgement = new TaskCompletionSource<bool>();
        var paths = new List<string>();
        IDictionary<string, object> payload = null;
        var timestamp = new object();
        using (var store = new FirebaseWalkCloudStore(() => "owner", (path, data) =>
        {
            paths.Add(path);
            payload = data;
            return acknowledgement.Task;
        }, timestamp))
        {
            var upload = store.UploadSummaryAsync("owner", Summary(), CancellationToken.None);
            Assert.That(upload.IsCompleted, Is.False, "Cache insertion is not acknowledgement.");
            acknowledgement.SetResult(true);
            await upload;
            await store.UploadSummaryAsync("owner", Summary(), CancellationToken.None);
            Assert.That(paths, Is.EqualTo(new[] { "users/owner/walks/walk-one", "users/owner/walks/walk-one" }));
            Assert.That(payload.Keys, Is.EquivalentTo(new[] { "schemaVersion", "id", "startedAtUtc",
                "endedAtUtc", "steps", "distanceMeters", "durationSeconds", "uploadedAt" }));
            Assert.That(payload["uploadedAt"], Is.SameAs(timestamp));
            Assert.That(payload["steps"], Is.EqualTo(120));
        }
    }

    [Test]
    public async Task UserChangesAreReflectedAndWrongOwnerCannotWrite()
    {
        var user = "owner";
        var writes = 0;
        using (var store = new FirebaseWalkCloudStore(() => user, (_, __) =>
        {
            writes++;
            return Task.CompletedTask;
        }, new object()))
        {
            Assert.That(store.AuthenticatedUserId, Is.EqualTo("owner"));
            user = "other";
            Assert.That(store.AuthenticatedUserId, Is.EqualTo("other"));
            await Expect<InvalidOperationException>(() => store.UploadSummaryAsync("owner", Summary(), CancellationToken.None));
            user = null;
            Assert.That(store.AuthenticatedUserId, Is.Empty);
            await Expect<InvalidOperationException>(() => store.UploadSummaryAsync("owner", Summary(), CancellationToken.None));
            Assert.That(writes, Is.Zero);
        }
    }

    [Test]
    public async Task PermissionFailureIsPropagated()
    {
        using (var store = new FirebaseWalkCloudStore(() => "owner",
            (_, __) => Task.FromException(new InvalidOperationException("Denied")), new object()))
            await Expect<InvalidOperationException>(() => store.UploadSummaryAsync("owner", Summary(), CancellationToken.None));
    }

    [Test]
    public async Task CancellationBeforeSubmissionDoesNotWrite()
    {
        var writes = 0;
        using (var cancellation = new CancellationTokenSource())
        using (var store = new FirebaseWalkCloudStore(() => "owner", (_, __) =>
        {
            writes++;
            return Task.CompletedTask;
        }, new object()))
        {
            cancellation.Cancel();
            await Expect<OperationCanceledException>(() => store.UploadSummaryAsync("owner", Summary(), cancellation.Token));
            Assert.That(writes, Is.Zero);
        }
    }

    [Test]
    public async Task CancellationReleasesOfflineWriteWithoutClaimingSuccess()
    {
        var acknowledgement = new TaskCompletionSource<bool>();
        using (var cancellation = new CancellationTokenSource())
        using (var store = new FirebaseWalkCloudStore(() => "owner", (_, __) => acknowledgement.Task, new object()))
        {
            var upload = store.UploadSummaryAsync("owner", Summary(), cancellation.Token);
            cancellation.Cancel();
            Assert.That(await Task.WhenAny(upload, Task.Delay(2000)), Is.SameAs(upload));
            await Expect<OperationCanceledException>(() => upload);
            Assert.That(acknowledgement.Task.IsCompleted, Is.False, "Underlying SDK writes cannot be canceled.");
            acknowledgement.SetException(new InvalidOperationException("Late server failure"));
        }
    }

    [Test]
    public async Task DisposalStopsPendingUploadAndHidesUser()
    {
        var acknowledgement = new TaskCompletionSource<bool>();
        var store = new FirebaseWalkCloudStore(() => "owner", (_, __) => acknowledgement.Task, new object());
        var upload = store.UploadSummaryAsync("owner", Summary(), CancellationToken.None);
        store.Dispose();
        store.Dispose();
        Assert.That(store.AuthenticatedUserId, Is.Empty);
        Assert.That(await Task.WhenAny(upload, Task.Delay(2000)), Is.SameAs(upload));
        await Expect<OperationCanceledException>(() => upload);
        await Expect<ObjectDisposedException>(() => store.UploadSummaryAsync("owner", Summary(), CancellationToken.None));
        acknowledgement.SetResult(true);
    }

    [Test]
    public async Task OfflineTimeoutLeavesWriteRetryable()
    {
        var acknowledgement = new TaskCompletionSource<bool>();
        using (var store = new FirebaseWalkCloudStore(() => "owner", (_, __) => acknowledgement.Task,
            new object(), TimeSpan.FromMilliseconds(10)))
        {
            await Expect<TimeoutException>(() => store.UploadSummaryAsync("owner", Summary(), CancellationToken.None));
            Assert.That(acknowledgement.Task.IsCompleted, Is.False);
            acknowledgement.SetResult(true);
        }
    }

    [Test]
    public async Task InvalidPathCannotWriteOutsideWalkCollection()
    {
        var writes = 0;
        using (var store = new FirebaseWalkCloudStore(() => "owner", (_, __) =>
        {
            writes++;
            return Task.CompletedTask;
        }, new object()))
        {
            var summary = Summary();
            summary.id = "one/other/two";
            await Expect<ArgumentException>(() => store.UploadSummaryAsync("owner", summary, CancellationToken.None));
            Assert.That(writes, Is.Zero);
        }
    }

    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        Assert.Fail("Expected " + typeof(T).Name);
    }

    private static WalkSummary Summary() => new WalkSummary
    {
        schemaVersion = 1, id = "walk-one", steps = 120,
        startedAtUtc = "2026-09-09T01:00:00Z", endedAtUtc = "2026-09-09T01:01:00Z",
        distanceMeters = 75.5f, durationSeconds = 60
    };
}
