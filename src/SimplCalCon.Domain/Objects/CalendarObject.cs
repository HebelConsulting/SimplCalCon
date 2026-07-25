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

    /// <summary>Extracted LOCATION, for list display/search (null when absent).</summary>
    public string? Location { get; set; }

    /// <summary>Master component start, converted to UTC (null for a task with no start).</summary>
    public DateTime? DtStartUtc { get; set; }

    /// <summary>Master component end (or start+duration), converted to UTC.</summary>
    public DateTime? DtEndUtc { get; set; }

    /// <summary>True for a date-only (all-day) component.</summary>
    public bool IsAllDay { get; set; }

    /// <summary>True when the component carries an RRULE/RDATE.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Extracted RRULE value (without the <c>RRULE:</c> prefix), for the web editor to load/round-trip (ADR 0050); null when not recurring.</summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>Indexed ORGANIZER/ATTENDEE rows, rebuilt from the blob on every write (ADR 0030).</summary>
    public ICollection<EventAttendee> Attendees { get; set; } = [];

    /// <summary>
    /// Occurrence-window index state (ADR 0061). True when every occurrence is materialized into
    /// <see cref="Occurrences"/> — a non-recurring event, or a bounded rule whose whole span fits the
    /// rolling window. When false, only <see cref="OccurrencesFromUtc"/>..<see cref="OccurrencesUntilUtc"/>
    /// is materialized and time-range queries outside that window fall back to on-the-fly expansion.
    /// </summary>
    public bool OccurrencesComplete { get; set; } = true;

    /// <summary>Lower bound of the materialized window (null when <see cref="OccurrencesComplete"/> covers all).</summary>
    public DateTime? OccurrencesFromUtc { get; set; }

    /// <summary>Upper bound of the materialized window (null when <see cref="OccurrencesComplete"/> covers all).</summary>
    public DateTime? OccurrencesUntilUtc { get; set; }

    /// <summary>Materialized occurrence rows for this recurring event (ADR 0061); empty for non-recurring.</summary>
    public ICollection<EventOccurrence> Occurrences { get; set; } = [];
}
