namespace SimplCalCon.Domain.Collections;

/// <summary>A calendar collection holding events (VEVENT) and/or tasks (VTODO).</summary>
public class Calendar : Collection
{
    public bool SupportsEvents { get; set; } = true;

    public bool SupportsTasks { get; set; } = true;

    /// <summary>Default IANA time zone id for the calendar (CalDAV calendar-timezone).</summary>
    public string? TimeZoneId { get; set; }
}
