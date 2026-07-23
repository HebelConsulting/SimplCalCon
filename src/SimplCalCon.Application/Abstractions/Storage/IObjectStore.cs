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
}

public sealed record PutObjectRequest(Guid CollectionId, string ResourceName, string Blob, Guid? AuthorPrincipalId);

public sealed record StoredObjectResult(
    Guid Id, string Uid, string ResourceName, Guid ETag, long RevisionNumber, bool Created);
