namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Creates/updates calendar events and contacts from structured fields for the REST
/// API + web UI (ADR 0009/0010): builds the iCalendar/vCard blob and stores it through
/// <see cref="IObjectStore"/>. On update the existing UID is preserved. (Unknown-property
/// preservation applies to DAV round-trips, not REST-authored objects.)
/// </summary>
public interface IObjectComposer
{
    Task<StoredObjectResult> PutEventAsync(
        Guid collectionId, string? resourceName, EventInput input, Guid? authorPrincipalId, CancellationToken cancellationToken);

    Task<StoredObjectResult> PutContactAsync(
        Guid collectionId, string? resourceName, ContactInput input, Guid? authorPrincipalId, CancellationToken cancellationToken);
}

public sealed record EventInput(
    string Summary,
    DateTime StartUtc,
    DateTime? EndUtc,
    bool IsAllDay,
    string? Location = null,
    string? Organizer = null,
    IReadOnlyList<AttendeeInput>? Attendees = null,
    Recurrence? Recurrence = null,
    string? RawRecurrenceRule = null);

public sealed record AttendeeInput(string Address, string? CommonName);

public sealed record ContactInput(
    string? FormattedName,
    string? FamilyName,
    string? GivenName,
    string? Organization,
    IReadOnlyList<string> Emails,
    IReadOnlyList<string> Phones);
