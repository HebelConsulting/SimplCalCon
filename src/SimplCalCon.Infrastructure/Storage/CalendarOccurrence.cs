using Ical.Net.DataTypes;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// On-the-fly recurrence expansion for CalDAV time-range matching (ADR 0021/CalDAV):
/// does any occurrence of the object start within [startUtc, endUtc)? Uses Ical.Net's
/// lazy occurrence stream, bounded by the range end.
/// </summary>
internal static class CalendarOccurrence
{
    public static bool OverlapsRange(string blob, DateTime? startUtc, DateTime? endUtc)
    {
        if (startUtc is not { } start || endUtc is not { } end)
        {
            // An unbounded (or half-open) query can't be narrowed here — include leniently.
            return true;
        }

        Ical.Net.Calendar? calendar;
        try
        {
            calendar = Ical.Net.Calendar.Load(blob);
        }
        catch (Exception)
        {
            return true;
        }

        if (calendar is null)
        {
            return true;
        }

        var from = new CalDateTime(DateTime.SpecifyKind(start, DateTimeKind.Utc));
        return calendar.GetOccurrences(from)
            .Select(occurrence => occurrence.Period.StartTime?.AsUtc)
            .TakeWhile(startTime => startTime is not null && startTime.Value < end)
            .Any();
    }

    /// <summary>
    /// Materializes occurrence windows starting within [fromUtc, toUtc) for the occurrence-window
    /// index (ADR 0061). Returns the windows plus <c>Truncated</c> — true when the series continues
    /// at or past <paramref name="toUtc"/> or the <paramref name="maxRows"/> cap was hit, so the
    /// caller knows the future horizon is not fully covered and time-range queries beyond it must
    /// fall back to on-the-fly expansion. A malformed blob yields none (and no truncation).
    /// </summary>
    public static (IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> Windows, bool Truncated) Materialize(
        string blob, DateTime fromUtc, DateTime toUtc, int maxRows)
    {
        Ical.Net.Calendar? calendar;
        try
        {
            calendar = Ical.Net.Calendar.Load(blob);
        }
        catch (Exception)
        {
            return ([], false);
        }

        if (calendar is null)
        {
            return ([], false);
        }

        var from = new CalDateTime(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        var windows = new List<(DateTime StartUtc, DateTime EndUtc)>();
        foreach (var occurrence in calendar.GetOccurrences(from))
        {
            if (occurrence.Period.StartTime?.AsUtc is not { } start)
            {
                continue;
            }

            if (start >= toUtc)
            {
                return (windows, true); // the series reaches past the window
            }

            if (windows.Count >= maxRows)
            {
                return (windows, true); // pathological rule — stop and fall back beyond here
            }

            var end = occurrence.Period.EndTime?.AsUtc ?? start;
            windows.Add((start, end));
        }

        return (windows, false); // the series ended within the window
    }

    /// <summary>
    /// Expands a component into its concrete occurrence windows starting within [fromUtc, toUtc)
    /// (ADR 0050/0051), for the web grid. Each item carries its RECURRENCE-ID (the canonical slot,
    /// so a per-instance edit/delete can target it — ADR 0051); a malformed blob yields none.
    /// </summary>
    public static IReadOnlyList<(DateTime StartUtc, DateTime EndUtc, DateTime RecurrenceIdUtc, string? Summary, string? Location)> Occurrences(
        string blob, DateTime fromUtc, DateTime toUtc)
    {
        Ical.Net.Calendar? calendar;
        try
        {
            calendar = Ical.Net.Calendar.Load(blob);
        }
        catch (Exception)
        {
            return [];
        }

        if (calendar is null)
        {
            return [];
        }

        // An overridden occurrence's displayed start may differ from its RECURRENCE-ID; map the
        // override's shown start back to its slot so re-editing targets the original occurrence.
        var overrideSlots = calendar.Events
            .Where(e => e.RecurrenceIdentifier is not null && e.DtStart is not null)
            .GroupBy(e => e.DtStart!.AsUtc)
            .ToDictionary(g => g.Key, g => g.First().RecurrenceIdentifier!.StartTime.AsUtc);

        var from = new CalDateTime(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        return calendar.GetOccurrences(from)
            .TakeWhile(o => o.Period.StartTime?.AsUtc is { } s && s < toUtc)
            .Where(o => o.Period.StartTime?.AsUtc is { } s && s >= fromUtc)
            .Select(o =>
            {
                var start = o.Period.StartTime!.AsUtc;
                var end = o.Period.EndTime?.AsUtc ?? start;
                var recurrenceId = overrideSlots.GetValueOrDefault(start, start);
                // An overridden occurrence's summary/location come from its own VEVENT, not the master.
                var source = o.Source as Ical.Net.CalendarComponents.CalendarEvent;
                return (start, end, recurrenceId, source?.Summary, source?.Location);
            })
            .ToList();
    }
}
