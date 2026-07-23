namespace SimplCalCon.Domain.Objects;

/// <summary>
/// An immutable prior state of a <see cref="CollectionObject"/> (ADR 0011): the blob
/// and ETag as of one write, with who made it and how. Appended on every create,
/// update, and delete.
/// </summary>
public class ObjectRevision
{
    public Guid Id { get; set; }

    public Guid ObjectId { get; set; }

    public CollectionObject? Object { get; set; }

    public long RevisionNumber { get; set; }

    public required string Blob { get; set; }

    /// <summary>The object's concurrency token (ETag) at this revision.</summary>
    public Guid ETag { get; set; }

    public RevisionOperation Operation { get; set; }

    /// <summary>The principal that made the change (null for anonymous/system writes).</summary>
    public Guid? AuthorPrincipalId { get; set; }

    public DateTime CreatedAt { get; set; }
}
