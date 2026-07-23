using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Api.Health;

/// <summary>Readiness probe: the configured database is reachable (ADR 0205-style split).</summary>
internal sealed class DatabaseHealthCheck(SimplCalConDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database is not reachable.");
}
