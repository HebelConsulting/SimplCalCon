using SimplCalCon.Client;

namespace SimplCalCon.WebTests;

/// <summary>Multi-day grid coverage (ADR 0072): which days an event's chip appears on.</summary>
public sealed class CalendarGridTests
{
    private static DateTime D(int day, int hour = 0) => new(2026, 7, day, hour, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Single_day_timed_event_covers_only_its_day()
    {
        var start = D(10, 9);
        var end = D(10, 10);
        Assert.True(CalendarGrid.CoversDay(start, end, D(10)));
        Assert.False(CalendarGrid.CoversDay(start, end, D(11)));
        Assert.False(CalendarGrid.CoversDay(start, end, D(9)));
    }

    [Fact]
    public void Timed_multi_day_event_covers_each_day_through_its_end()
    {
        var start = D(10, 9);   // Fri 09:00
        var end = D(12, 14);    // Sun 14:00
        Assert.True(CalendarGrid.CoversDay(start, end, D(10)));
        Assert.True(CalendarGrid.CoversDay(start, end, D(11))); // full middle day
        Assert.True(CalendarGrid.CoversDay(start, end, D(12))); // partial last day
        Assert.False(CalendarGrid.CoversDay(start, end, D(13)));
    }

    [Fact]
    public void Event_ending_exactly_at_midnight_does_not_cover_that_day()
    {
        var start = D(10, 9);
        var end = D(11);        // Sat 00:00 — end is exclusive
        Assert.True(CalendarGrid.CoversDay(start, end, D(10)));
        Assert.False(CalendarGrid.CoversDay(start, end, D(11)));
    }

    [Fact]
    public void All_day_multi_day_event_covers_up_to_but_not_the_exclusive_dtend()
    {
        // All-day Mon..Thu means DTSTART 13 00:00, DTEND 16 00:00 (exclusive) → covers 13, 14, 15.
        var start = D(13);
        var end = D(16);
        Assert.True(CalendarGrid.CoversDay(start, end, D(13)));
        Assert.True(CalendarGrid.CoversDay(start, end, D(14)));
        Assert.True(CalendarGrid.CoversDay(start, end, D(15)));
        Assert.False(CalendarGrid.CoversDay(start, end, D(16)));
    }

    [Fact]
    public void Missing_or_zero_duration_covers_only_the_start_day()
    {
        Assert.True(CalendarGrid.CoversDay(D(10, 9), null, D(10)));
        Assert.False(CalendarGrid.CoversDay(D(10, 9), null, D(11)));
        Assert.True(CalendarGrid.CoversDay(D(10, 9), D(10, 9), D(10))); // zero-length
    }
}
