using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;

// Call from Unity's main thread after Firebase dependency initialization.
public sealed class FirebaseWalkCloudStore : IWalkCloudStore, IDisposable
{
    private readonly Func<string> currentUserId;
    private readonly Func<string, IDictionary<string, object>, Task> setDocument;
    private readonly object serverTimestamp;
    private readonly TimeSpan acknowledgementTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private bool disposed;

    public FirebaseWalkCloudStore(FirebaseAuth auth, FirebaseFirestore firestore)
    {
        if (auth == null) throw new ArgumentNullException(nameof(auth));
        if (firestore == null) throw new ArgumentNullException(nameof(firestore));
        currentUserId = () => auth.CurrentUser?.UserId ?? "";
        setDocument = (path, data) => firestore.Document(path).SetAsync(data);
        serverTimestamp = FieldValue.ServerTimestamp;
    }

    // Test seam: exercise the actual adapter without contacting Firebase.
    internal FirebaseWalkCloudStore(Func<string> currentUserId,
        Func<string, IDictionary<string, object>, Task> setDocument, object serverTimestamp,
        TimeSpan? acknowledgementTimeout = null)
    {
        this.currentUserId = currentUserId ?? throw new ArgumentNullException(nameof(currentUserId));
        this.setDocument = setDocument ?? throw new ArgumentNullException(nameof(setDocument));
        this.serverTimestamp = serverTimestamp ?? throw new ArgumentNullException(nameof(serverTimestamp));
        this.acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(30);
    }

    public string AuthenticatedUserId => disposed ? "" : currentUserId() ?? "";

    public async Task UploadSummaryAsync(string ownerUserId, WalkSummary summary,
        CancellationToken cancellationToken)
    {
        if (disposed) throw new ObjectDisposedException(nameof(FirebaseWalkCloudStore));
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(ownerUserId) || ownerUserId != AuthenticatedUserId)
            throw new InvalidOperationException("Sign in as the walk owner before uploading.");
        ValidateDocumentId(ownerUserId);
        if (summary == null) throw new ArgumentNullException(nameof(summary));
        ValidateDocumentId(summary.id);

        // Explicit allowlist: never serialize the full local walk or its route.
        var fields = new Dictionary<string, object>
        {
            ["schemaVersion"] = summary.schemaVersion,
            ["id"] = summary.id,
            ["startedAtUtc"] = summary.startedAtUtc,
            ["endedAtUtc"] = summary.endedAtUtc,
            ["steps"] = summary.steps,
            ["distanceMeters"] = summary.distanceMeters,
            ["durationSeconds"] = summary.durationSeconds,
            ["uploadedAt"] = serverTimestamp
        };

        var destructionToken = lifetime.Token;
        using (var waitLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destructionToken))
        {
            waitLifetime.Token.ThrowIfCancellationRequested();
            // An offline SDK write can wait indefinitely. Bound our wait so the
            // queue can recover and other accounts can sync. This does NOT cancel
            // Firebase's underlying write; the stable document path makes retries safe.
            var write = setDocument($"users/{ownerUserId}/walks/{summary.id}", fields);
            var deadline = Task.Delay(acknowledgementTimeout, waitLifetime.Token);
            var finished = await Task.WhenAny(write, deadline);
            if (finished != write)
            {
                ObserveCompletion(write);
                waitLifetime.Token.ThrowIfCancellationRequested();
                throw new TimeoutException("Cloud acknowledgement is still pending.");
            }
            waitLifetime.Cancel();
            await write; // SetAsync completes on server acknowledgement, not cache insertion.
            cancellationToken.ThrowIfCancellationRequested();
            destructionToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private static async void ObserveCompletion(Task write)
    {
        try { await write; }
        catch (Exception) { } // The durable queue already records this attempt as retryable.
    }

    private static void ValidateDocumentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains("/")
            || value == "." || value == "..")
            throw new ArgumentException("Invalid cloud document ID.");
    }
}
