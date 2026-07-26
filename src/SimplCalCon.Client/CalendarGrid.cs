namespace SimplCalCon.Client;

/// <summary>Pure date maths for the calendar grid (ADR 0072).</summary>
public static class CalendarGrid
{
    /// <summary>
    /// Does an event with local interval [start, end) cover <paramref name="day"/>? A multi-day event
    /// covers every day from its start to its last day; the end is exclusive at midnight, so an all-day
    /// event's DTEND date — or a timed event ending exactly at 00:00 — does not add a day. A missing,
    /// zero, or negative duration covers only the start day.
    /// </summary>
    public static bool CoversDay(DateTime localStart, DateTime? localEnd, DateTime day)
    {
        var startDay = localStart.Date;
        if (localEnd is not { } end || end <= localStart)
        {
            return day.Date == startDay;
        }

        var lastDay = end.TimeOfDay == TimeSpan.Zero ? end.Date.AddDays(-1) : end.Date;
        if (lastDay < startDay)
        {
            lastDay = startDay;
        }

        return day.Date >= startDay && day.Date <= lastDay;
    }
}
