using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Walk = StepCountAndGpsManager.SavedWalkSession;

// Called on Unity's main thread. Each file contains both the completed walk and
// its queue state, so a crash cannot leave a saved walk absent from a second queue.
public sealed class LocalWalkRepository
{
    public const int CurrentSchemaVersion = 1;
    public string DirectoryPath { get; }
    private readonly Action<string> reportWarning;

    public LocalWalkRepository(string directoryPath, Action<string> reportWarning = null)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        this.reportWarning = reportWarning ?? (message => Debug.LogWarning(message));
    }

    public string Save(Walk walk)
    {
        NormalizeAndValidate(walk);
        var path = GetPath(walk.id);
        if (File.Exists(path) || File.Exists(path + ".bak"))
        {
            if (!TryLoad(path, out var existing))
                throw new IOException("The existing walk is unreadable; it has been preserved.");

            // Completed walks are immutable. Repeat saves preserve owner and sync
            // state; a stale caller cannot reset an acknowledged upload to pending.
            if (PayloadJson(existing) != PayloadJson(walk))
                throw new InvalidOperationException("A completed walk cannot be changed. Use a new walk ID.");
            return path;
        }

        WriteAtomically(path, walk);
        return path;
    }

    // The manager uses a separate Active directory; these never enter the upload queue.
    internal void SaveCheckpoint(Walk walk) => WriteAtomically(GetPath(walk.id), walk);

    internal void RemoveCheckpoint(string id)
    {
        var path = GetPath(id);
        foreach (var suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    public List<string> GetFilePaths()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return new List<string>();
            var paths = new HashSet<string>(Directory.GetFiles(DirectoryPath, "walk_*.json"), StringComparer.Ordinal);
            foreach (var backup in Directory.GetFiles(DirectoryPath, "walk_*.json.bak"))
                paths.Add(backup.Substring(0, backup.Length - 4));
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            reportWarning("Could not list saved walks: " + exception.GetType().Name);
            return new List<string>();
        }
    }

    public List<Walk> LoadAll()
    {
        var byId = new Dictionary<string, Walk>(StringComparer.Ordinal);
        var canonicalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in GetFilePaths())
        {
            if (!TryLoad(path, out var walk)) continue;
            var isCanonical = string.Equals(path, GetPath(walk.id), StringComparison.OrdinalIgnoreCase);
            // Older versions used timestamp filenames. Show one row per ID and
            // prefer the canonical file containing the current upload state.
            if (!canonicalIds.Contains(walk.id) || isCanonical) byId[walk.id] = walk;
            if (isCanonical) canonicalIds.Add(walk.id);
        }
        return byId.Values.OrderByDescending(walk => ParseUtc(walk.startedAtUtc)).ToList();
    }

    public bool TryLoad(string path, out Walk walk)
    {
        walk = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(path), DirectoryPath, StringComparison.OrdinalIgnoreCase))
                return false;
            if (TryRead(path, out walk, out var unsupportedVersion)) return true;
            // A newer app's primary record must not be rolled back to an older
            // backup just because this version cannot understand it.
            if (unsupportedVersion) return false;
            if (TryRead(path + ".bak", out walk))
            {
                reportWarning("Recovered a saved walk from its backup.");
                return true;
            }
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            reportWarning("Could not load saved walk: " + exception.GetType().Name);
        }
        return false;
    }

    public Walk Find(string id) => LoadAll().Find(walk => walk.id == id);

    // The future account UI calls this only for a specific local walk that the
    // player chose to import. Sign-in by itself must never claim unowned data.
    public void AssignOwner(string id, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) throw new ArgumentException("An authenticated owner is required.");
        var walk = Find(id) ?? throw new InvalidOperationException("Walk not found.");
        if (!string.IsNullOrEmpty(walk.ownerUserId) && walk.ownerUserId != ownerUserId)
            throw new InvalidOperationException("A walk cannot be reassigned to another account.");
        walk.ownerUserId = ownerUserId;
        WriteAtomically(GetPath(id), walk);
    }

    public void UpdateSync(string id, string ownerUserId, WalkSyncMetadata sync)
    {
        var walk = Find(id) ?? throw new InvalidOperationException("Walk not found.");
        if (string.IsNullOrEmpty(ownerUserId) || walk.ownerUserId != ownerUserId)
            throw new InvalidOperationException("The walk owner does not match the upload owner.");
        walk.sync = sync;
        WriteAtomically(GetPath(id), walk);
    }

    public static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var date) || date.Offset != TimeSpan.Zero)
            throw new ArgumentException("Walk timestamps must be valid UTC dates.");
        return date;
    }

    private string GetPath(string id)
    {
        ValidateId(id);
        return Path.Combine(DirectoryPath, "walk_" + id + ".json");
    }

    private bool TryRead(string path, out Walk walk)
    {
        return TryRead(path, out walk, out _);
    }

    private bool TryRead(string path, out Walk walk, out bool unsupportedVersion)
    {
        walk = null;
        unsupportedVersion = false;
        if (!File.Exists(path)) return false;
        try
        {
            var candidate = JsonUtility.FromJson<Walk>(File.ReadAllText(path));
            unsupportedVersion = candidate != null && (candidate.schemaVersion < 0 || candidate.schemaVersion > CurrentSchemaVersion);
            NormalizeAndValidate(candidate);
            walk = candidate;
            return true;
        }
        catch (Exception exception) when (IsFileError(exception) || exception is ArgumentException)
        {
            reportWarning("Skipped an unreadable or unsupported walk file: " + Path.GetFileName(path));
            return false;
        }
    }

    private void WriteAtomically(string path, Walk walk)
    {
        NormalizeAndValidate(walk);
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = path + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(walk, true));
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        // Never delete the destination before committing its replacement.
        if (File.Exists(path))
        {
            // Don't replace a healthy backup with a damaged primary file.
            var primaryIsValid = TryRead(path, out _, out var unsupportedVersion);
            if (unsupportedVersion) throw new IOException("A newer walk format exists; it has been preserved.");
            File.Replace(temporaryPath, path, primaryIsValid ? path + ".bak" : null);
        }
        else File.Move(temporaryPath, path);
    }

    private static void NormalizeAndValidate(Walk walk)
    {
        if (walk == null) throw new ArgumentException("Missing walk record.");
        if (walk.schemaVersion < 0 || walk.schemaVersion > CurrentSchemaVersion)
            throw new ArgumentException("Unsupported walk schema.");
        ValidateId(walk.id);
        if (ParseUtc(walk.endedAtUtc) < ParseUtc(walk.startedAtUtc))
            throw new ArgumentException("Walk ends before it starts.");
        if (walk.steps < 0 || !Nonnegative(walk.distanceMeters) || !Nonnegative(walk.durationSeconds)
            || !Coordinate(walk.finalLatitude, 90f) || !Coordinate(walk.finalLongitude, 180f)
            || !Nonnegative(walk.finalAccuracyMeters)) throw new ArgumentException("Invalid walk measurements.");
        walk.EnsureCollections();
        if (walk.trackingVersion < 0 || walk.trackingVersion > 1 || walk.nativeSequence < 0)
            throw new ArgumentException("Unsupported tracking metadata.");
        float previousSeconds = 0;
        foreach (var point in walk.routePoints)
        {
            if (point == null || !Coordinate(point.latitude, 90f) || !Coordinate(point.longitude, 180f)
                || !Nonnegative(point.accuracyMeters) || !Nonnegative(point.secondsSinceSessionStart)
                || !WalkGpsFilter.Finite(point.gpsTimestamp) || point.gpsTimestamp < 0
                || (walk.trackingVersion > 0 && (point.secondsSinceSessionStart < previousSeconds
                    || point.secondsSinceSessionStart > walk.durationSeconds)))
                throw new ArgumentException("Invalid route point.");
            previousSeconds = point.secondsSinceSessionStart;
        }
        if (walk.routePointCount != walk.routePoints.Count) throw new ArgumentException("Route count does not match.");
        walk.schemaVersion = CurrentSchemaVersion; // Existing JSON without a version is version zero.
        walk.ownerUserId = walk.ownerUserId ?? "";
        walk.sync = walk.sync ?? new WalkSyncMetadata();
        if (!Enum.IsDefined(typeof(WalkUploadState), walk.sync.state) || walk.sync.attempts < 0)
            throw new ArgumentException("Invalid upload state.");
        if (!string.IsNullOrEmpty(walk.sync.nextAttemptUtc)) ParseUtc(walk.sync.nextAttemptUtc);
        if (walk.sync.state == WalkUploadState.Synced)
        {
            if (string.IsNullOrWhiteSpace(walk.ownerUserId)) throw new ArgumentException("Synced walk has no owner.");
            ParseUtc(walk.sync.syncedAtUtc);
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 || id.Any(c =>
                !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')))
            throw new ArgumentException("Invalid walk ID.");
    }

    private static bool Nonnegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    private static bool Coordinate(float value, float limit) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= limit;
    private static bool IsFileError(Exception exception) => exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException;

    private static string PayloadJson(Walk walk)
    {
        var copy = JsonUtility.FromJson<Walk>(JsonUtility.ToJson(walk));
        copy.ownerUserId = "";
        copy.sync = null;
        return JsonUtility.ToJson(copy);
    }
}
