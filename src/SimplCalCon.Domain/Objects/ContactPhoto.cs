using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Domain.Objects;

/// <summary>
/// A cached copy of a contact card's external PHOTO (ADR 0037). A 1:1 shared-primary-key
/// companion to the contact object (<see cref="ObjectId"/> is both PK and FK), so a card whose
/// PHOTO is a URL keeps its photo even when the source dies or throttles. On a persistent source
/// failure the cached bytes are embedded back into the card, making it self-contained.
/// </summary>
public class ContactPhoto
{
    /// <summary>The contact object this photo belongs to — both PK and FK.</summary>
    public Guid ObjectId { get; set; }

    public CollectionObject Object { get; set; } = null!;

    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>The cached image bytes.</summary>
    public required byte[] Photo { get; set; }

    public required string ContentType { get; set; }

    /// <summary>The URL the bytes were fetched from; the cache is invalid when the card's PHOTO URL changes.</summary>
    public required string SourceUrl { get; set; }

    public DateTime FetchedAt { get; set; }
}
