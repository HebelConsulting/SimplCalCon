using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Bound from <c>SimplCalCon:ContactPhotos</c> (ADR 0057).</summary>
public sealed class ContactPhotoOptions
{
    /// <summary>Runs the background refresh (on by default; set false to disable).</summary>
    public bool RefreshEnabled { get; set; } = true;

    /// <summary>Refresh cadence in hours (floored at 1).</summary>
    public int RefreshHours { get; set; } = 24;

    /// <summary>Max stale photos refreshed per cycle.</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Periodically refreshes stale external contact-photo caches (ADR 0057) so photos stay fresh and
/// dead source URLs self-heal (the cached bytes are embedded into the card) without waiting for a
/// view. On by default; a failed cycle is logged and retried next interval.
/// </summary>
internal sealed class ContactPhotoRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<ContactPhotoOptions> options,
    ILogger<ContactPhotoRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RefreshEnabled)
        {
            logger.LogInformation("Contact photo refresh disabled (SimplCalCon:ContactPhotos:RefreshEnabled).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.RefreshHours));
        var batchSize = Math.Max(1, options.Value.BatchSize);
        logger.LogInformation("Contact photo refresh started (every {Hours}h, up to {Batch}/cycle).", interval.TotalHours, batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var photos = scope.ServiceProvider.GetRequiredService<IContactPhotoService>();
                var refreshed = await photos.RefreshStaleAsync(batchSize, stoppingToken);
                if (refreshed > 0)
                {
                    logger.LogInformation("Refreshed {Count} stale contact photo(s).", refreshed);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Contact photo refresh cycle failed.");
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
