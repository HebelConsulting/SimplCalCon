using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

public sealed class CalendarOccurrenceTests
{
    private const string Weekly =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:w1\r\n" +
        "DTSTART:20260907T090000Z\r\nDTEND:20260907T093000Z\r\nSUMMARY:Standup\r\n" +
        "RRULE:FREQ=WEEKLY;COUNT=4\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static readonly DateTime Sep1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Oct5 = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Occurrences_expands_weekly_within_the_window()
    {
        var occurrences = CalendarOccurrence.Occurrences(Weekly, Sep1, Oct5);

        Assert.Equal(4, occurrences.Count);
        Assert.Equal([7, 14, 21, 28], occurrences.Select(o => o.StartUtc.Day).OrderBy(d => d).ToList());
        // For a non-overridden occurrence the RECURRENCE-ID equals its start.
        Assert.All(occurrences, o => Assert.Equal(o.StartUtc, o.RecurrenceIdUtc));
        Assert.All(occurrences, o => Assert.Equal("Standup", o.Summary));
    }

    [Fact]
    public void Occurrences_window_excludes_outside_the_range()
    {
        // Only the first two weeks (Sep 7 + 14).
        var occurrences = CalendarOccurrence.Occurrences(Weekly, Sep1, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, occurrences.Count);
    }

    [Fact]
    public void Overlaps_range_true_inside_false_outside()
    {
        Assert.True(CalendarOccurrence.OverlapsRange(Weekly, Sep1, Oct5));
        Assert.False(CalendarOccurrence.OverlapsRange(Weekly, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Malformed_blob_yields_no_occurrences()
    {
        Assert.Empty(CalendarOccurrence.Occurrences("not a calendar", Sep1, Oct5));
    }

    [Fact]
    public void Window_is_start_inclusive_and_end_exclusive()
    {
        // [Sep 14 09:00, Sep 21 09:00): includes Sep 14 (== from), excludes Sep 21 (== to) and Sep 7 (< from).
        var from = new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
        var occurrences = CalendarOccurrence.Occurrences(Weekly, from, to);
        Assert.Single(occurrences);
        Assert.Equal(14, occurrences[0].StartUtc.Day);
    }

    [Fact]
    public void Overlaps_range_with_open_bounds_is_lenient()
    {
        Assert.True(CalendarOccurrence.OverlapsRange(Weekly, null, null));
        Assert.True(CalendarOccurrence.OverlapsRange(Weekly, Sep1, null));
    }

    [Fact]
    public void Materialize_bounded_series_is_not_truncated()
    {
        var (windows, truncated) = CalendarOccurrence.Materialize(Weekly, Sep1, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2000);

        Assert.Equal(4, windows.Count);
        Assert.False(truncated); // COUNT=4 series ends inside the window
    }

    [Fact]
    public void Materialize_unbounded_series_truncates_at_the_window_end()
    {
        const string dailyUnbounded =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:d1\r\n" +
            "DTSTART:20260907T090000Z\r\nDTEND:20260907T093000Z\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var to = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

        var (windows, truncated) = CalendarOccurrence.Materialize(dailyUnbounded, Sep1, to, 2000);

        Assert.True(truncated);                            // series continues past the window
        Assert.All(windows, w => Assert.True(w.StartUtc < to));
    }

    [Fact]
    public void Materialize_stops_at_the_row_cap()
    {
        const string dailyUnbounded =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:d1\r\n" +
            "DTSTART:20260907T090000Z\r\nDTEND:20260907T093000Z\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var (windows, truncated) = CalendarOccurrence.Materialize(dailyUnbounded, Sep1, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5);

        Assert.Equal(5, windows.Count);
        Assert.True(truncated); // cap hit before the window end
    }

    [Fact]
    public void An_override_uses_its_own_start_and_summary_but_the_original_recurrence_id()
    {
        const string withOverride =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:w1\r\n" +
            "DTSTART:20260907T090000Z\r\nDTEND:20260907T093000Z\r\nSUMMARY:Standup\r\nRRULE:FREQ=WEEKLY;COUNT=4\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:w1\r\nRECURRENCE-ID:20260914T090000Z\r\n" +
            "DTSTART:20260914T100000Z\r\nDTEND:20260914T103000Z\r\nSUMMARY:Moved\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var moved = CalendarOccurrence.Occurrences(withOverride, Sep1, Oct5).Single(o => o.Summary == "Moved");
        Assert.Equal(10, moved.StartUtc.Hour);       // the override's shown start
        Assert.Equal(9, moved.RecurrenceIdUtc.Hour); // the original slot it replaces
    }
}
