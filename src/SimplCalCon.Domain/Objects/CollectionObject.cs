using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Common;

namespace SimplCalCon.Domain.Objects;

/// <summary>
/// A stored calendar object or contact. The <see cref="Blob"/> (raw iCalendar/vCard)
/// is the source of truth; subtypes add extracted, indexed fields (ADR 0004). Carries
/// the sync bookkeeping (change number, tombstone) and a running revision number;
/// prior revisions live in <see cref="Revisions"/> (ADR 0011). Mapped table-per-hierarchy.
/// </summary>
public abstract class CollectionObject : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    public Guid CollectionId { get; set; }

    public Collection? Collection { get; set; }

    /// <summary>The iCalendar/vCard UID; unique within the collection.</summary>
    public required string Uid { get; set; }

    /// <summary>DAV resource name (e.g. <c>{uid}.ics</c>); unique within the collection.</summary>
    public required string ResourceName { get; set; }

    /// <summary>The verbatim iCalendar/vCard payload — the source of truth.</summary>
    public required string Blob { get; set; }

    /// <summary>Running revision counter, incremented on every write.</summary>
    public long RevisionNumber { get; set; }

    /// <summary>The collection's <see cref="Collection.ChangeSequence"/> at this object's last change.</summary>
    public long ChangeNumber { get; set; }

    /// <summary>Tombstone flag: a deleted object is retained so sync can report the removal.</summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public ICollection<ObjectRevision> Revisions { get; set; } = [];
}
