using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Infrastructure.Identity;

/// <summary>Bound from <c>SimplCalCon:Auth</c> (ADR 0079).</summary>
public sealed class TokenPruneOptions
{
    /// <summary>
    /// Prune OpenIddict tokens/authorizations that are invalid or expired and older than this many days;
    /// 0 = disabled. Default 14 — matches the refresh-token lifetime (ADR 0076). Non-destructive: only
    /// already-invalid rows (e.g. redeemed rolling-refresh tokens) are removed, never a valid session.
    /// </summary>
    public int TokenPruneDays { get; set; } = 14;

    /// <summary>Prune cadence in hours (floored at 1).</summary>
    public int PruneHours { get; set; } = 24;
}

/// <summary>
/// Periodically prunes stale OpenIddict tokens and authorizations (ADR 0079). Rolling refresh tokens
/// (ADR 0076) leave redeemed rows behind on every renewal; without pruning the <c>OpenIddictTokens</c>
/// table grows unbounded. On by default (opt-out via <c>TokenPruneDays = 0</c>) since it only removes
/// already-invalid rows. A failed cycle is logged and retried next interval.
/// </summary>
internal sealed class TokenPruneService(
    IServiceScopeFactory scopeFactory,
    IOptions<TokenPruneOptions> options,
    ILogger<TokenPruneService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = options.Value.TokenPruneDays;
        if (days <= 0)
        {
            logger.LogInformation("OpenIddict token pruning disabled (SimplCalCon:Auth:TokenPruneDays).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.PruneHours));
        logger.LogInformation("OpenIddict token pruning started (invalid/expired older than {Days}d, every {Hours}h).",
            days, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
                var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
                var threshold = clock.UtcNow.AddDays(-days);

                // Order matters: tokens first, then authorizations (an authorization with tokens still
                // attached is not pruned) — per the OpenIddict manager docs.
                var prunedTokens = await tokens.PruneAsync(threshold, stoppingToken);
                var prunedAuthorizations = await authorizations.PruneAsync(threshold, stoppingToken);
                if (prunedTokens > 0 || prunedAuthorizations > 0)
                {
                    logger.LogInformation(
                        "OpenIddict prune: removed {Tokens} token(s) and {Authorizations} authorization(s) older than {Days}d.",
                        prunedTokens, prunedAuthorizations, days);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenIddict token pruning failed.");
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
