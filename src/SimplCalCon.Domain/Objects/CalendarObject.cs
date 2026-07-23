namespace SimplCalCon.Domain.Objects;

/// <summary>
/// A calendar object (one VEVENT or VTODO series, including its recurrence overrides,
/// as a single resource). Extracted fields are stored UTC for range querying; the
/// exact original times live in the blob.
/// </summary>
public class CalendarObject : CollectionObject
{
    public CalendarComponentType ComponentType { get; set; }

    public string? Summary { get; set; }

    /// <summary>Master component start, converted to UTC (null for a task with no start).</summary>
    public DateTime? DtStartUtc { get; set; }

    /// <summary>Master component end (or start+duration), converted to UTC.</summary>
    public DateTime? DtEndUtc { get; set; }

    /// <summary>True for a date-only (all-day) component.</summary>
    public bool IsAllDay { get; set; }

    /// <summary>True when the component carries an RRULE/RDATE.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Indexed ORGANIZER/ATTENDEE rows, rebuilt from the blob on every write (ADR 0030).</summary>
    public ICollection<EventAttendee> Attendees { get; set; } = [];
}
