using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class WalkSyncService
{
    private readonly LocalWalkRepository repository;
    private readonly IWalkCloudStore cloud;
    private readonly Func<DateTimeOffset> utcNow;
    private bool running;

    public bool IsRunning => running;

    public WalkSyncService(LocalWalkRepository repository, IWalkCloudStore cloud,
        Func<DateTimeOffset> utcNow = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    // Run on the Unity main thread; await keeps its synchronization context.
    // Called on startup, resume, walk completion, and periodically by the manager.
    public async Task SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        if (running || string.IsNullOrWhiteSpace(cloud.AuthenticatedUserId)) return;
        running = true;
        var owner = cloud.AuthenticatedUserId;
        try
        {
            foreach (var walk in repository.LoadAll())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cloud.AuthenticatedUserId != owner) break;
                if (walk.ownerUserId != owner || walk.sync.state == WalkUploadState.Synced) continue;
                if (!string.IsNullOrEmpty(walk.sync.nextAttemptUtc)
                    && LocalWalkRepository.ParseUtc(walk.sync.nextAttemptUtc) > utcNow()) continue;

                // Persist the retry first. Process termination during upload leaves
                // the walk retryable, including when server acknowledgement is lost.
                var sync = walk.sync;
                sync.attempts = Math.Min(sync.attempts, 1000000) + 1;
                sync.state = WalkUploadState.RetryNeeded;
                sync.nextAttemptUtc = utcNow().AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(10, sync.attempts - 1)))).ToString("o");
                repository.UpdateSync(walk.id, owner, sync);
                try
                {
                    await cloud.UploadSummaryAsync(owner, WalkSummary.FromWalk(walk), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    // Keep the durable retry state. Do not store backend error
                    // strings, which can contain tokens or other account details.
                    continue;
                }

                sync.state = WalkUploadState.Synced;
                sync.nextAttemptUtc = "";
                sync.syncedAtUtc = utcNow().ToString("o");
                repository.UpdateSync(walk.id, owner, sync);
            }
        }
        finally { running = false; }
    }
}
