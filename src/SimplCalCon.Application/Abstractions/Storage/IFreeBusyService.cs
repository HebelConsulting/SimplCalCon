namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Computes a user's busy windows for free/busy (ADR 0030): the merged, opaque busy
/// periods across the calendars they own, with recurrence expanded. Backs the REST
/// <c>/free-busy</c>, the CalDAV <c>free-busy-query</c> REPORT, and the RFC 6638
/// schedule-outbox free-busy POST. Address resolution maps a calendar-user address
/// (<c>mailto:</c>) to a local user in the tenant.
/// </summary>
public interface IFreeBusyService
{
    Task<IReadOnlyList<BusyPeriod>> GetBusyAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>Resolves a calendar-user address to a local, active user in the tenant; null if unknown.</summary>
    Task<Guid?> ResolveUserAsync(Guid tenantId, string calendarUserAddress, CancellationToken cancellationToken);
}

public sealed record BusyPeriod(DateTime StartUtc, DateTime EndUtc);
