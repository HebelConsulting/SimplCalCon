namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>Another resource in the collection already uses the object's UID.</summary>
public sealed class UidConflictException(string uid)
    : ObjectStoreException($"Another resource in the collection already uses UID '{uid}'.");
