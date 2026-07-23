namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>The submitted iCalendar/vCard payload could not be parsed or lacks a UID.</summary>
public sealed class MalformedObjectException(string detail)
    : ObjectStoreException($"The object payload is malformed: {detail}");
