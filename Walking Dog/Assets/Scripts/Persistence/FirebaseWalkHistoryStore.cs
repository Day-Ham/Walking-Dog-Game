using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;

internal sealed class FirebaseWalkHistoryStore : IWalkHistoryStore
{
    internal const int PageSize = 20;
    private readonly Func<string> currentUser;
    private readonly Func<string, WalkHistoryCursor, Task<WalkHistoryPage>> fetch;
    private readonly TimeSpan timeout;
    public string AuthenticatedUserId => currentUser() ?? "";

    public FirebaseWalkHistoryStore(FirebaseAuth auth, FirebaseFirestore firestore)
        : this(() => auth.CurrentUser?.UserId, (owner, cursor) => FetchAsync(firestore, owner, cursor)) { }

    internal FirebaseWalkHistoryStore(Func<string> currentUser,
        Func<string, WalkHistoryCursor, Task<WalkHistoryPage>> fetch, TimeSpan? timeout = null)
    {
        this.currentUser = currentUser;
        this.fetch = fetch;
        this.timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<WalkHistoryPage> ReadAsync(string owner, WalkHistoryCursor cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckOwner(owner);
        if (cursor != null && cursor.Owner != owner) throw new InvalidOperationException("History belongs to another account.");
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var read = fetch(owner, cursor);
            var deadline = Task.Delay(timeout, wait.Token);
            if (await Task.WhenAny(read, deadline) != read)
            {
                ObserveCompletion(read);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Walk history is unavailable.");
            }
            wait.Cancel();
            var page = await read;
            cancellationToken.ThrowIfCancellationRequested();
            CheckOwner(owner);
            return page;
        }
    }

    private void CheckOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner != AuthenticatedUserId || owner.Contains("/") || owner == "." || owner == "..")
            throw new InvalidOperationException("Sign in to view your walks.");
    }

    private static async Task<WalkHistoryPage> FetchAsync(FirebaseFirestore firestore, string owner, WalkHistoryCursor cursor)
    {
        // A single-field order uses Firestore's standard index. DocumentSnapshot
        // cursors include the ID tie-breaker, including equal start timestamps.
        Query query = firestore.Collection($"users/{owner}/walks").OrderByDescending("startedAtUtc").Limit(PageSize);
        if (cursor != null) query = query.StartAfter((DocumentSnapshot)cursor.Position);
        var snapshot = await query.GetSnapshotAsync(Source.Server);
        var documents = snapshot.Documents.ToList();
        var entries = new List<WalkHistoryEntry>();
        foreach (var document in documents)
            if (WalkHistoryEntry.TryParse(document.Id, document.ToDictionary(), out var entry)) entries.Add(entry);
        // Advance using the last raw document even when it could not be parsed.
        var next = documents.Count == PageSize ? new WalkHistoryCursor(owner, documents[documents.Count - 1]) : null;
        return new WalkHistoryPage(entries, next, documents.Count - entries.Count);
    }

    private static async void ObserveCompletion(Task task)
    {
        try { await task; }
        catch (Exception) { } // Native reads cannot be cancelled; stale results are discarded.
    }
}
