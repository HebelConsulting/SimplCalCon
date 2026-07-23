using SimplCalCon.Domain.Common;
using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Domain.Principals;

/// <summary>
/// The identity that ownership and ACL grants point at (ADR 0007). A principal is
/// either a <see cref="User"/> or a <see cref="Group"/>; both share a single id
/// space so a grant target is unambiguous. Mapped table-per-hierarchy.
/// See docs/adr/0006 and docs/adr/0007.
/// </summary>
public abstract class Principal : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning tenant. Null only for a platform-administrator <see cref="User"/>,
    /// which operates outside every tenant; always set for a <see cref="Group"/>.
    /// </summary>
    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
