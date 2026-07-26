using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Bound from <c>SimplCalCon:Retention</c> (ADR 0060).</summary>
public sealed class RetentionOptions
{
    /// <summary>Purge trashed objects soft-deleted more than this many days ago; 0 = keep forever (disabled).</summary>
    public int TrashRetentionDays { get; set; }

    /// <summary>Hard-purge collections soft-deleted more than this many days ago; 0 = keep forever (disabled) — ADR 0077.</summary>
    public int DeletedCollectionRetentionDays { get; set; }

    /// <summary>Prune live-object revision history older than this many days; 0 = keep all (disabled) — ADR 0080.</summary>
    public int RevisionRetentionDays { get; set; }

    /// <summary>When pruning old revisions, always keep at least this many most-recent per object (the newest is always kept regardless) — ADR 0080.</summary>
    public int MaxRevisionsPerObject { get; set; }

    /// <summary>Sweep cadence in hours (floored at 1).</summary>
    public int SweepHours { get; set; } = 24;

    /// <summary>Objects purged per batch (the sweep drains all eligible each cycle).</summary>
    public int BatchSize { get; set; } = 500;
}

/// <summary>
/// Periodically purges trashed objects (ADR 0060) and long-deleted collections (ADR 0077) past their
/// retention windows. Each is independently opt-in (its *RetentionDays &gt; 0); the service idles if both
/// are off (destructive — opt-in). Each cycle drains all eligible rows in batches; a failed cycle is
/// logged and retried next interval.
/// </summary>
internal sealed class RetentionSweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var trashDays = options.Value.TrashRetentionDays;
        var collectionDays = options.Value.DeletedCollectionRetentionDays;
        var revisionDays = options.Value.RevisionRetentionDays;
        // Always keep at least the newest revision; MaxRevisionsPerObject raises that floor.
        var keepMinimum = Math.Max(1, options.Value.MaxRevisionsPerObject);
        if (trashDays <= 0 && collectionDays <= 0 && revisionDays <= 0)
        {
            logger.LogInformation(
                "Retention sweep disabled (SimplCalCon:Retention:TrashRetentionDays / DeletedCollectionRetentionDays / RevisionRetentionDays).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.SweepHours));
        var batchSize = Math.Max(1, options.Value.BatchSize);
        logger.LogInformation(
            "Retention sweep started (trash {TrashDays}d, deleted collections {CollectionDays}d, revisions {RevisionDays}d/keep≥{Keep} — 0 = off; every {Hours}h).",
            trashDays, collectionDays, revisionDays, keepMinimum, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var now = clock.UtcNow.UtcDateTime;

                if (trashDays > 0)
                {
                    var total = await DrainAsync(
                        (cutoff, ct) => retention.PurgeTrashedBeforeAsync(cutoff, batchSize, ct),
                        now.AddDays(-trashDays), batchSize, stoppingToken);
                    if (total > 0)
                    {
                        logger.LogInformation("Retention: purged {Count} trashed object(s) older than {Days}d.", total, trashDays);
                    }
                }

                if (collectionDays > 0)
                {
                    var total = await DrainAsync(
                        (cutoff, ct) => retention.PurgeDeletedCollectionsBeforeAsync(cutoff, batchSize, ct),
                        now.AddDays(-collectionDays), batchSize, stoppingToken);
                    if (total > 0)
                    {
                        logger.LogInformation("Retention: purged {Count} deleted collection(s) older than {Days}d.", total, collectionDays);
                    }
                }

                if (revisionDays > 0)
                {
                    var total = await DrainAsync(
                        (cutoff, ct) => retention.PruneRevisionsAsync(cutoff, keepMinimum, batchSize, ct),
                        now.AddDays(-revisionDays), batchSize, stoppingToken);
                    if (total > 0)
                    {
                        logger.LogInformation(
                            "Retention: pruned revision history for {Count} object(s) (older than {Days}d, keeping ≥{Keep}).",
                            total, revisionDays, keepMinimum);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention sweep failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    // Drains all eligible rows for one purge kind in batches (a full batch means there may be more).
    private static async Task<int> DrainAsync(
        Func<DateTime, CancellationToken, Task<int>> purge, DateTime cutoff, int batchSize, CancellationToken stoppingToken)
    {
        var total = 0;
        int purged;
        do
        {
            purged = await purge(cutoff, stoppingToken);
            total += purged;
        }
        while (purged == batchSize && !stoppingToken.IsCancellationRequested);
        return total;
    }
}
