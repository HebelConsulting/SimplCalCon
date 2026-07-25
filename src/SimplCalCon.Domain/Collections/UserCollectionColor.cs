namespace SimplCalCon.Domain.Collections;

/// <summary>
/// A user's personal colour override for a collection (ADR 0066): layered on top of the collection's
/// owner-set <see cref="Collection.Color"/>. Effective colour for a user = this override, else the
/// owner colour, else a palette fallback. Unique per (user, collection).
/// </summary>
public class UserCollectionColor
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CollectionId { get; set; }

    /// <summary>Hex colour (<c>#RRGGBB</c>).</summary>
    public required string Color { get; set; }
}
