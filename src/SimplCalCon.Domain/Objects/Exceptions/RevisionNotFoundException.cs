namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>A requested prior revision of an object does not exist (ADR 0028).</summary>
public sealed class RevisionNotFoundException(Guid objectId, long revisionNumber)
    : ObjectStoreException($"Object '{objectId}' has no revision {revisionNumber}.");
