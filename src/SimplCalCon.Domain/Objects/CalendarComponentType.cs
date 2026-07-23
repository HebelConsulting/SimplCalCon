namespace SimplCalCon.Domain.Objects;

/// <summary>The primary component of a <see cref="CalendarObject"/>.</summary>
public enum CalendarComponentType
{
    /// <summary>VEVENT.</summary>
    Event = 0,

    /// <summary>VTODO.</summary>
    Todo = 1,
}
