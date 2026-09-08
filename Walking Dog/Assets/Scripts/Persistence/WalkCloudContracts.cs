using System;
using System.Threading;
using System.Threading.Tasks;
using Walk = StepCountAndGpsManager.SavedWalkSession;

public enum WalkUploadState
{
    Pending = 0,
    RetryNeeded = 1,
    Synced = 2
}

[Serializable]
public sealed class WalkSyncMetadata
{
    public WalkUploadState state;
    public int attempts;
    public string nextAttemptUtc = "";
    public string syncedAtUtc = "";
}

// Deliberately excludes coordinates, route samples, and local queue metadata.
// A future Firebase adapter maps this DTO to users/{ownerUserId}/walks/{id}.
[Serializable]
public sealed class WalkSummary
{
    public int schemaVersion;
    public string id;
    public string startedAtUtc;
    public string endedAtUtc;
    public int steps;
    public float distanceMeters;
    public float durationSeconds;

    public static WalkSummary FromWalk(Walk walk)
    {
        return new WalkSummary
        {
            schemaVersion = walk.schemaVersion,
            id = walk.id,
            startedAtUtc = walk.startedAtUtc,
            endedAtUtc = walk.endedAtUtc,
            steps = walk.steps,
            distanceMeters = walk.distanceMeters,
            durationSeconds = walk.durationSeconds
        };
    }
}

public interface IWalkCloudStore
{
    // Empty while signed out or before authentication has finished.
    string AuthenticatedUserId { get; }

    // Complete ONLY after server acknowledgement. Repeated calls with the same
    // owner + walk ID must upsert the same document, never add another record or
    // increment rewards/totals. Verify owner still matches the authenticated user.
    Task UploadSummaryAsync(string ownerUserId, WalkSummary summary, CancellationToken cancellationToken);
}
