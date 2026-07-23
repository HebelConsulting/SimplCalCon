using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Indexed fields pulled from a blob for one object.</summary>
internal abstract record ExtractedObject(string Uid);

internal sealed record ExtractedCalendarObject(
    string Uid,
    CalendarComponentType Component,
    string? Summary,
    DateTime? DtStartUtc,
    DateTime? DtEndUtc,
    bool IsAllDay,
    bool IsRecurring) : ExtractedObject(Uid);

internal sealed record ExtractedContact(
    string Uid,
    string? FormattedName,
    string? FamilyName,
    string? GivenName,
    string? Organization,
    string? Emails,
    string? Phones) : ExtractedObject(Uid);
