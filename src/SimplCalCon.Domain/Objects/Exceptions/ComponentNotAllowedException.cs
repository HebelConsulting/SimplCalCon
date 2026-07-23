namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>
/// The object's component type isn't accepted by the target collection (e.g. a VTODO
/// put into a calendar that only supports events, or a calendar object into an
/// address book).
/// </summary>
public sealed class ComponentNotAllowedException(string component)
    : ObjectStoreException($"The collection does not accept components of type '{component}'.");
