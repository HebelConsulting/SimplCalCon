namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>The target collection does not exist (or has been deleted).</summary>
public sealed class CollectionNotFoundException(Guid collectionId)
    : ObjectStoreException($"Collection '{collectionId}' was not found.");
