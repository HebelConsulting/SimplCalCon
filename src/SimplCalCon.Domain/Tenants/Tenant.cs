using SimplCalCon.Domain.Common;

namespace SimplCalCon.Domain.Tenants;

/// <summary>
/// The hard isolation boundary of the deployment: every principal, collection,
/// and object belongs to exactly one tenant. See
/// docs/adr/0006-multi-tenancy-and-administration.md.
/// </summary>
public class Tenant : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    /// <summary>Human-readable display name.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Stable, unique reference handle for administration. Not part of any URL —
    /// tenant routing is derived from the authenticated principal (ADR 0006).
    /// </summary>
    public required string Slug { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
