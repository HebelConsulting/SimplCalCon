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
    /// Expands a component into its concrete occurrence windows starting within [fromUtc, toUtc)
    /// (ADR 0050), for the web grid. Returns (start, end) UTC pairs; a malformed blob yields none.
    /// </summary>
    public static IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> Occurrences(
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

        var from = new CalDateTime(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        return calendar.GetOccurrences(from)
            .TakeWhile(o => o.Period.StartTime?.AsUtc is { } s && s < toUtc)
            .Where(o => o.Period.StartTime?.AsUtc is { } s && s >= fromUtc)
            .Select(o => (
                o.Period.StartTime!.AsUtc,
                o.Period.EndTime?.AsUtc ?? o.Period.StartTime!.AsUtc))
            .ToList();
    }
}
