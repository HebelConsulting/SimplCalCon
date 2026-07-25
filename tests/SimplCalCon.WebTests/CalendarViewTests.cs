using Bunit;
using SimplCalCon.Client.Pages;

namespace SimplCalCon.WebTests;

/// <summary>
/// End-to-end render guards for the Calendar tab's merged, colour-coded multi-collection view
/// (ADR 0062/0063): the pane lists every calendar, the list shows entries from all checked
/// calendars with a colour + calendar column, activating switches the highlight, and unchecking
/// filters a calendar's events out.
/// </summary>
public sealed class CalendarViewTests : TestContext
{
    private static readonly Guid Work = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Personal = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private IRenderedComponent<CalendarView> RenderCalendar()
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            ["/api/calendars"] = ApiHarness.List(
                new { id = Work, name = "Work", color = "#ff0000", supportsEvents = true, supportsTasks = true, shared = false },
                new { id = Personal, name = "Personal", color = (string?)null, supportsEvents = true, supportsTasks = true, shared = false }),
            [$"/api/calendars/{Work}/events"] = ApiHarness.List(
                new { id = Guid.NewGuid(), summary = "Standup", startUtc = "2026-07-15T09:00:00Z", endUtc = "2026-07-15T09:30:00Z", isAllDay = false, isRecurring = false }),
            [$"/api/calendars/{Personal}/events"] = ApiHarness.List(
                new { id = Guid.NewGuid(), summary = "Gym", startUtc = "2026-07-16T18:00:00Z", endUtc = "2026-07-16T19:00:00Z", isAllDay = false, isRecurring = false }),
        });

        return RenderComponent<CalendarView>();
    }

    [Fact]
    public void Pane_lists_every_calendar()
    {
        var cut = RenderCalendar();
        Assert.Equal(["Work", "Personal"], cut.FindAll(".coll-name-text").Select(n => n.TextContent));
    }

    [Fact]
    public void List_merges_events_from_all_checked_calendars_with_colour_and_calendar_columns()
    {
        var cut = RenderCalendar();

        var rows = cut.FindAll(".event-table tbody tr");
        Assert.Equal(2, rows.Count); // one from each calendar

        // Every row carries a colour swatch and its owning calendar's name.
        Assert.All(rows, r => Assert.Single(r.QuerySelectorAll(".color-col .swatch")));
        Assert.Contains("Standup", cut.Markup);
        Assert.Contains("Gym", cut.Markup);
        Assert.Contains("Work", cut.Markup);
        Assert.Contains("Personal", cut.Markup);
        // Work's explicit colour tints its swatch.
        Assert.Contains("background:#ff0000", cut.Markup);
    }

    [Fact]
    public void Activating_a_calendar_moves_the_highlight()
    {
        var cut = RenderCalendar();
        Assert.Contains("active", cut.FindAll(".coll-row")[0].ClassList); // Work active by default (first)

        cut.FindAll(".coll-name")[1].Click(); // activate Personal

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("active", cut.FindAll(".coll-row")[0].ClassList);
            Assert.Contains("active", cut.FindAll(".coll-row")[1].ClassList);
        });
    }

    [Fact]
    public void Unchecking_a_calendar_filters_its_events_out()
    {
        var cut = RenderCalendar();
        Assert.Contains("Standup", cut.Markup);

        cut.FindAll(".coll-check")[0].Change(false); // hide Work

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Standup", cut.Markup); // Work's event is gone
            Assert.Contains("Gym", cut.Markup);           // Personal's remains
        });
    }
}
