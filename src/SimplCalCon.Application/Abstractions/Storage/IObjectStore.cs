namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// The single write path for calendar/contact objects (ADR 0004): parse → validate →
/// store blob → extract indexed fields → new revision + bump the collection change
/// sequence, all transactionally. The blob is the source of truth.
/// </summary>
public interface IObjectStore
{
    Task<StoredObjectResult> PutAsync(PutObjectRequest request, CancellationToken cancellationToken);

    /// <summary>Tombstones the object (retained so sync can report the removal). Returns false if absent.</summary>
    Task<bool> DeleteAsync(
        Guid collectionId, string resourceName, Guid? authorPrincipalId, CancellationToken cancellationToken);

    /// <summary>
    /// Restores a trashed object or reinstates a prior revision (ADR 0028): pass
    /// <paramref name="revisionNumber"/> to reinstate that version's blob, or null to bring
    /// the current tombstone back as-is. Returns null if the object doesn't exist.
    /// </summary>
    Task<StoredObjectResult?> RestoreAsync(
        Guid collectionId, string resourceName, long? revisionNumber, Guid? authorPrincipalId, CancellationToken cancellationToken);

    /// <summary>Permanently removes one trashed object and its revision history. Returns false if it isn't in the trash.</summary>
    Task<bool> PurgeAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken);

    /// <summary>Permanently removes every trashed object (and its revisions) in a collection. Returns the count purged.</summary>
    Task<int> PurgeTrashAsync(Guid collectionId, CancellationToken cancellationToken);
}

public sealed record PutObjectRequest(Guid CollectionId, string ResourceName, string Blob, Guid? AuthorPrincipalId);

public sealed record StoredObjectResult(
    Guid Id, string Uid, string ResourceName, Guid ETag, long RevisionNumber, bool Created);
