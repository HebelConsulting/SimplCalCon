namespace SimplCalCon.Domain.Objects;

/// <summary>
/// An indexed ORGANIZER/ATTENDEE of a calendar event (ADR 0030). The iCal blob stays the
/// source of truth; these rows are rebuilt from it on every write to make attendees
/// queryable (free/busy resolution now, scheduling delivery later). The ORGANIZER is
/// modelled as a row with <see cref="IsOrganizer"/> = true.
/// </summary>
public class EventAttendee
{
    public Guid Id { get; set; }

    public Guid ObjectId { get; set; }

    public CalendarObject? Object { get; set; }

    /// <summary>The calendar-user address, e.g. <c>mailto:bob@example.com</c>.</summary>
    public required string Address { get; set; }

    /// <summary>Upper-cased address for case-insensitive matching (never a DB collation).</summary>
    public required string NormalizedAddress { get; set; }

    public string? CommonName { get; set; }

    public AttendeeRole Role { get; set; }

    public ParticipationStatus ParticipationStatus { get; set; }

    public bool IsOrganizer { get; set; }
}

/// <summary>iCalendar <c>ROLE</c> of an attendee (RFC 5545).</summary>
public enum AttendeeRole
{
    Chair,
    RequiredParticipant,
    OptionalParticipant,
    NonParticipant,
}

/// <summary>iCalendar <c>PARTSTAT</c> of an attendee (RFC 5545).</summary>
public enum ParticipationStatus
{
    NeedsAction,
    Accepted,
    Declined,
    Tentative,
    Delegated,
}
