using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

internal sealed class WalkHistoryEntry
{
    public string Id { get; }
    public DateTimeOffset StartedAt { get; }
    public int Steps { get; }
    public double DistanceMeters { get; }
    public double DurationSeconds { get; }

    // Constructs a historical walk entry record.
    private WalkHistoryEntry(string id, DateTimeOffset startedAt, int steps, double distance, double duration)
    {
        Id = id;
        StartedAt = startedAt;
        Steps = steps;
        DistanceMeters = distance;
        DurationSeconds = duration;
    }

    // Parses a Firestore document dictionary into a strongly typed walk history entry.
    public static bool TryParse(string documentId, IDictionary<string, object> fields, out WalkHistoryEntry entry)
    {
        entry = null;
        if (fields == null || !fields.TryGetValue("schemaVersion", out var schema) || !(schema is long version) || version != 1
            || !fields.TryGetValue("id", out var id) || !(id is string storedId) || storedId != documentId
            || !fields.TryGetValue("startedAtUtc", out var start) || !(start is string startText)
            || !DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startedAt)
            || !fields.TryGetValue("steps", out var steps) || !(steps is long stepCount) || stepCount < 0 || stepCount > int.MaxValue
            || !TryMeasurement(fields, "distanceMeters", out var distance)
            || !TryMeasurement(fields, "durationSeconds", out var duration)) return false;
        entry = new WalkHistoryEntry(documentId, startedAt, (int)stepCount, distance, duration);
        return true;
    }

    // Helper to safely parse non-negative numeric fields (distance/duration) from Firestore dictionary.
    private static bool TryMeasurement(IDictionary<string, object> fields, string key, out double number)
    {
        number = 0;
        if (!fields.TryGetValue(key, out var raw)) return false;
        if (raw is double floating) number = floating;
        else if (raw is long integer) number = integer;
        else return false;
        return !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0 && number <= float.MaxValue;
    }

    public string DateLabel => StartedAt.ToLocalTime().ToString("MMM d, yyyy  •  h:mm tt", CultureInfo.CurrentCulture);
    public string StatsLabel
    {
        get
        {
            var distance = DistanceMeters < 1000 ? $"{DistanceMeters:0} m" : $"{DistanceMeters / 1000:0.00} km";
            // Avoid TimeSpan overflow for valid but very large historical values.
            var duration = DurationSeconds < 60 ? $"{DurationSeconds:0} sec"
                : DurationSeconds < 3600 ? $"{Math.Floor(DurationSeconds / 60):0} min {Math.Floor(DurationSeconds % 60):0} sec"
                : $"{Math.Floor(DurationSeconds / 3600):0} hr {Math.Floor(DurationSeconds % 3600 / 60):0} min";
            return $"{Steps:N0} steps   •   {distance}   •   {duration}";
        }
    }
}

internal sealed class WalkHistoryCursor
{
    public string Owner { get; }
    public object Position { get; }
    // Constructs a pagination cursor for walking history queries.
    public WalkHistoryCursor(string owner, object position) { Owner = owner; Position = position; }
}

internal sealed class WalkHistoryPage
{
    public IReadOnlyList<WalkHistoryEntry> Entries { get; }
    public WalkHistoryCursor NextCursor { get; }
    public int SkippedCount { get; }
    // Constructs a page of walk history entries and optional continuation cursor.
    public WalkHistoryPage(IReadOnlyList<WalkHistoryEntry> entries, WalkHistoryCursor nextCursor, int skippedCount = 0)
    {
        Entries = entries;
        NextCursor = nextCursor;
        SkippedCount = skippedCount;
    }
}

internal interface IWalkHistoryStore
{
    string AuthenticatedUserId { get; }
    Task<WalkHistoryPage> ReadAsync(string owner, WalkHistoryCursor cursor, CancellationToken cancellationToken);
}

// Owns pagination and invalidates in-flight results when refreshing, closing,
// or switching accounts. It never inserts downloaded summaries into the upload queue.
internal sealed class WalkHistorySession : IDisposable
{
    private readonly IWalkHistoryStore store;
    private readonly List<WalkHistoryEntry> entries = new List<WalkHistoryEntry>();
    private readonly HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
    private CancellationTokenSource pending;
    private WalkHistoryCursor cursor;
    private int generation;
    private bool disposed;
    public string Owner { get; private set; } = "";
    public IReadOnlyList<WalkHistoryEntry> Entries => entries;
    public bool IsLoading { get; private set; }
    public bool HasMore { get; private set; }
    public int SkippedCount { get; private set; }
    public string Error { get; private set; } = "";

    // Creates a paginated walk history session for the active user account.
    public WalkHistorySession(IWalkHistoryStore store) { this.store = store; Reset(); }

    // Reconciles session ownership with current auth user and resets pagination if changed.
    public bool SynchronizeAccount()
    {
        if (Owner == (store.AuthenticatedUserId ?? "")) return false;
        Reset();
        return true;
    }

    // Clears stored walk entries, cancels pending requests, and resets pagination cursors.
    public void Reset()
    {
        generation++;
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        Owner = store.AuthenticatedUserId ?? "";
        entries.Clear();
        ids.Clear();
        cursor = null;
        HasMore = true;
        IsLoading = false;
        SkippedCount = 0;
        Error = "";
    }

    // Resets pagination state and fetches the first page of walk records.
    public Task RefreshAsync() { Reset(); return LoadMoreAsync(); }

    // Fetches the next page of walk entries using the current pagination cursor.
    public async Task LoadMoreAsync()
    {
        if (disposed) return;
        SynchronizeAccount();
        if (IsLoading || !HasMore || string.IsNullOrEmpty(Owner)) return;
        var revision = generation;
        var owner = Owner;
        pending?.Dispose();
        pending = new CancellationTokenSource();
        var token = pending.Token;
        IsLoading = true;
        Error = "";
        try
        {
            var page = await store.ReadAsync(owner, cursor, token);
            if (disposed || revision != generation || token.IsCancellationRequested || SynchronizeAccount()) return;
            foreach (var entry in page.Entries)
                if (ids.Add(entry.Id)) entries.Add(entry);
            cursor = page.NextCursor;
            HasMore = cursor != null;
            SkippedCount += page.SkippedCount;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!disposed && revision == generation && !SynchronizeAccount())
                Error = "Couldn't load walks. Check your connection and try again.";
        }
        finally
        {
            if (revision == generation) IsLoading = false;
        }
    }

    // Disposes the history session and releases background cancellation tokens.
    public void Dispose() { if (disposed) return; Reset(); disposed = true; }
}
