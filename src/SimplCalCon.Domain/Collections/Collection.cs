using SimplCalCon.Domain.Common;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Domain.Collections;

/// <summary>
/// A calendar or address book: owned by one user, holds objects, and carries the
/// per-collection change sequence that backs the CTag and sync-collection
/// (ADR 0004, 0012). Mapped table-per-hierarchy.
/// </summary>
public abstract class Collection : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>The owning user (a <see cref="Principal"/>).</summary>
    public Guid OwnerId { get; set; }

    public User? Owner { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Display colour for the collection's entries as a hex string (<c>#RRGGBB</c>); null = the UI auto-assigns one (ADR 0062).</summary>
    public string? Color { get; set; }

    /// <summary>Unguessable capability token for the read-only subscription feed (ADR 0069); null = feed disabled.</summary>
    public string? FeedToken { get; set; }

    /// <summary>DAV path segment for the collection; unique within the owner's home set.</summary>
    public required string ResourceName { get; set; }

    /// <summary>Monotonically increasing; bumped on any child add/modify/delete. Backs the CTag.</summary>
    public long ChangeSequence { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public ICollection<CollectionObject> Objects { get; set; } = [];
}
