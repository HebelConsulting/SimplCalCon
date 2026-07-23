namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>Base for storage-layer errors when writing calendar/contact objects (ADR 0004).</summary>
public abstract class ObjectStoreException(string message) : Exception(message);
