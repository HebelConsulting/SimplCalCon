using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.UnitTests;

public sealed class RecurrenceRuleTests
{
    [Theory]
    [InlineData("FREQ=DAILY", "DAILY", 1)]
    [InlineData("FREQ=WEEKLY;INTERVAL=2", "WEEKLY", 2)]
    [InlineData("FREQ=MONTHLY;COUNT=5", "MONTHLY", 1)]
    public void Parses_supported_rules(string rule, string frequency, int interval)
    {
        Assert.True(RecurrenceRule.TryParse(rule, out var recurrence));
        Assert.Equal(frequency, recurrence.Frequency);
        Assert.Equal(interval, recurrence.Interval);
    }

    [Fact]
    public void Parses_weekly_byday_and_count()
    {
        Assert.True(RecurrenceRule.TryParse("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=10", out var recurrence));
        Assert.Equal(["MO", "WE", "FR"], recurrence.ByDay);
        Assert.Equal(10, recurrence.Count);
        Assert.Null(recurrence.UntilUtc);
    }

    [Fact]
    public void Parses_until()
    {
        Assert.True(RecurrenceRule.TryParse("FREQ=WEEKLY;UNTIL=20260901T000000Z", out var recurrence));
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), recurrence.UntilUtc);
    }

    [Fact]
    public void Parses_monthly_by_month_day()
    {
        Assert.True(RecurrenceRule.TryParse("FREQ=MONTHLY;BYMONTHDAY=15", out var recurrence));
        Assert.Equal(15, recurrence.ByMonthDay);
        Assert.Empty(recurrence.ByDay);
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=2TU", "2TU")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "-1FR")]
    public void Parses_monthly_nth_weekday(string rule, string token)
    {
        Assert.True(RecurrenceRule.TryParse(rule, out var recurrence));
        Assert.Equal([token], recurrence.ByDay);
        Assert.Null(recurrence.ByMonthDay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FREQ=SECONDLY")]                       // unsupported frequency
    [InlineData("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO")]   // BYSETPOS beyond the editor
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15;BYDAY=MO")] // day-of-month and weekday together
    [InlineData("FREQ=MONTHLY;BYDAY=5TU")]              // 5th weekday not modelled (only 1..4, -1)
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU")]            // multiple ordinal weekdays not modelled
    [InlineData("FREQ=DAILY;BYDAY=MO")]                 // BYDAY only modelled for weekly/monthly
    [InlineData("FREQ=WEEKLY;BYDAY=2MO")]               // ordinal BYDAY not modelled for weekly
    [InlineData("FREQ=WEEKLY;COUNT=3;UNTIL=20260101T000000Z")] // COUNT and UNTIL are exclusive
    public void Rejects_unsupported_rules(string? rule)
    {
        Assert.False(RecurrenceRule.TryParse(rule, out _));
    }

    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE")]
    [InlineData("FREQ=MONTHLY;COUNT=5")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15")]
    [InlineData("FREQ=MONTHLY;BYDAY=2TU")]
    [InlineData("FREQ=MONTHLY;INTERVAL=3;BYDAY=-1FR")]
    [InlineData("FREQ=YEARLY;UNTIL=20301231T000000Z")]
    public void Format_round_trips_parse(string rule)
    {
        Assert.True(RecurrenceRule.TryParse(rule, out var recurrence));
        Assert.Equal(rule, RecurrenceRule.Format(recurrence));
    }

    [Fact]
    public void Format_omits_interval_of_one()
    {
        var recurrence = new Recurrence("WEEKLY", 1, ["MO"], null, null);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", RecurrenceRule.Format(recurrence));
    }
}
