using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Rolls the occurrence-window index forward as real time advances (ADR 0061): an object materialized
/// long ago has an aging future horizon, so near-future queries increasingly fall back. This sweep
/// re-materializes incomplete objects whose future coverage has dropped within
/// <see cref="OccurrenceOptions.RefreshBelowDays"/> of "now", in batches, each batch transactional so a
/// crash never leaves "covered" flags with missing rows. Correctness never depends on it — the query
/// fallback is always available; this just keeps the fast path fresh.
/// </summary>
internal sealed class OccurrenceRollForwardService(
    IServiceScopeFactory scopeFactory,
    IOptions<OccurrenceOptions> options,
    ILogger<OccurrenceRollForwardService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.RollForwardEnabled)
        {
            logger.LogInformation("Occurrence-index roll-forward disabled (SimplCalCon:Occurrences:RollForwardEnabled).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, opts.RollForwardHours));
        var batchSize = Math.Max(1, opts.RollForwardBatch);
        logger.LogInformation(
            "Occurrence-index roll-forward started (refresh below {Days}d of coverage, every {Hours}h).",
            opts.RefreshBelowDays, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var total = 0;
                int refreshed;
                do
                {
                    refreshed = await SweepBatchAsync(batchSize, opts.RefreshBelowDays, stoppingToken);
                    total += refreshed;
                }
                while (refreshed == batchSize && !stoppingToken.IsCancellationRequested);

                if (total > 0)
                {
                    logger.LogDebug("Occurrence-index roll-forward re-materialized {Count} object(s).", total);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Occurrence-index roll-forward failed.");
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

    private async Task<int> SweepBatchAsync(int batchSize, int refreshBelowDays, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<OccurrenceIndexer>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow.UtcDateTime;
        var threshold = now.AddDays(refreshBelowDays);

        // Incomplete, still-live recurring objects whose future coverage is running out — plus any
        // never-materialized (UntilUtc null: e.g. rows that predate the index migration), which this
        // sweep backfills. Most-aged first.
        var due = await dbContext.CalendarObjects
            .Where(o => !o.IsDeleted && o.IsRecurring && !o.OccurrencesComplete
                && (o.OccurrencesUntilUtc == null || o.OccurrencesUntilUtc < threshold))
            .OrderBy(o => o.OccurrencesUntilUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var calendarObject in due)
        {
            await indexer.RollForwardAsync(calendarObject, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }
}
