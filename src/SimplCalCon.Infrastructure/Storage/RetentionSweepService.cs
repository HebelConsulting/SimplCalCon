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
    /// <summary>Purge trashed objects soft-deleted more than this many days ago; 0 = keep forever (sweep disabled).</summary>
    public int TrashRetentionDays { get; set; }

    /// <summary>Sweep cadence in hours (floored at 1).</summary>
    public int SweepHours { get; set; } = 24;

    /// <summary>Objects purged per batch (the sweep drains all eligible each cycle).</summary>
    public int BatchSize { get; set; } = 500;
}

/// <summary>
/// Periodically purges trashed objects past the retention window (ADR 0060). Disabled unless
/// <see cref="RetentionOptions.TrashRetentionDays"/> is set (destructive — opt-in). Each cycle
/// drains all eligible objects in batches; a failed cycle is logged and retried next interval.
/// </summary>
internal sealed class RetentionSweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = options.Value.TrashRetentionDays;
        if (days <= 0)
        {
            logger.LogInformation("Retention sweep disabled (SimplCalCon:Retention:TrashRetentionDays).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.SweepHours));
        var batchSize = Math.Max(1, options.Value.BatchSize);
        logger.LogInformation("Retention sweep started (trash older than {Days}d, every {Hours}h).", days, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var cutoff = clock.UtcNow.UtcDateTime.AddDays(-days);

                var total = 0;
                int purged;
                do
                {
                    purged = await retention.PurgeTrashedBeforeAsync(cutoff, batchSize, stoppingToken);
                    total += purged;
                }
                while (purged == batchSize && !stoppingToken.IsCancellationRequested);

                if (total > 0)
                {
                    logger.LogInformation("Retention: purged {Count} trashed object(s) older than {Days}d.", total, days);
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
}
