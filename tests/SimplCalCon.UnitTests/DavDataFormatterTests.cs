using System.Text.RegularExpressions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

public sealed class DavDataFormatterTests
{
    private readonly DavDataFormatter _formatter = new();

    private const string Event =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:e1\r\n" +
        "DTSTART:20260715T090000Z\r\nSUMMARY:Team\r\nDESCRIPTION:Notes\r\nLOCATION:Room\r\n" +
        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nEND:VALARM\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private const string Weekly =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:w1\r\n" +
        "DTSTART:20260907T090000Z\r\nSUMMARY:Standup\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private const string Vcard =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:c1\r\nFN:Jane Doe\r\nN:Doe;Jane;;;\r\n" +
        "EMAIL:jane@example\r\nTEL:+15550001\r\nEND:VCARD\r\n";

    // A calendar with a VEVENT and a VTODO, each carrying its own VALARM (ACTION + TRIGGER).
    private const string EventAndTodo =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:e1\r\nSUMMARY:Ev\r\nDESCRIPTION:d\r\n" +
        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT\r\n" +
        "BEGIN:VTODO\r\nUID:t1\r\nSUMMARY:Td\r\n" +
        "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER:-PT5M\r\nEND:VALARM\r\nEND:VTODO\r\n" +
        "END:VCALENDAR\r\n";

    private static IReadOnlySet<string> Props(params string[] p) => new HashSet<string>(p, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, DavCompSelection> Tree(params (string Name, DavCompSelection Sel)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Sel, StringComparer.OrdinalIgnoreCase);

    private static DavCompSelection Sel(
        bool allProps, bool allComps, IReadOnlySet<string>? props = null, IReadOnlyDictionary<string, DavCompSelection>? comps = null) =>
        new(allProps, allComps, props ?? Props(), comps ?? Tree());

    [Fact]
    public void Full_request_returns_the_blob_unchanged()
    {
        Assert.Equal(Event, _formatter.FormatCalendar(Event, CalendarDataRequest.Full));
        Assert.Equal(Vcard, _formatter.FormatContact(Vcard, AddressDataRequest.Full));
    }

    [Fact]
    public void Calendar_prop_subset_keeps_requested_and_drops_the_rest()
    {
        var request = new CalendarDataRequest(
            Sel(false, true, comps: Tree(("VEVENT", Sel(false, false, Props("SUMMARY"))))),
            null);

        var result = _formatter.FormatCalendar(Event, request);
        Assert.Contains("SUMMARY:Team", result);
        Assert.Contains("UID:e1", result);       // always kept
        Assert.Contains("VERSION:2.0", result);  // always kept
        Assert.DoesNotContain("DESCRIPTION", result);
        Assert.DoesNotContain("LOCATION", result);
        Assert.DoesNotContain("VALARM", result); // sub-component not listed → dropped
    }

    [Fact]
    public void Contact_prop_subset_keeps_only_requested_properties()
    {
        var result = _formatter.FormatContact(Vcard, new AddressDataRequest(Props("FN")));
        Assert.Contains("FN:Jane Doe", result);
        Assert.Contains("UID:c1", result);
        Assert.DoesNotContain("EMAIL", result);
        Assert.DoesNotContain("TEL", result);
    }

    [Fact]
    public void All_props_keeps_every_property_of_the_component()
    {
        var request = new CalendarDataRequest(
            Sel(false, true, comps: Tree(("VEVENT", Sel(allProps: true, allComps: false)))),
            null);

        var result = _formatter.FormatCalendar(Event, request);
        Assert.Contains("DESCRIPTION:Notes", result); // AllProps keeps it even though not listed
        Assert.Contains("LOCATION:Room", result);
        Assert.DoesNotContain("VALARM", result);      // AllComps:false still drops the sub-component
    }

    [Fact]
    public void Subset_keeps_the_vtimezone_component_even_when_not_listed()
    {
        const string withTz =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VTIMEZONE\r\nTZID:Europe/Zurich\r\nEND:VTIMEZONE\r\n" +
            "BEGIN:VEVENT\r\nUID:e1\r\nSUMMARY:Team\r\nDESCRIPTION:Notes\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var request = new CalendarDataRequest(
            Sel(false, true, comps: Tree(("VEVENT", Sel(false, false, Props("SUMMARY"))))),
            null);

        var result = _formatter.FormatCalendar(withTz, request);
        Assert.Contains("VTIMEZONE", result);
        Assert.Contains("TZID:Europe/Zurich", result);
        Assert.DoesNotContain("DESCRIPTION", result);
    }

    [Fact]
    public void Deep_nesting_keeps_only_the_selected_property_of_a_nested_subcomponent()
    {
        // VEVENT → VALARM → keep ACTION only.
        var request = new CalendarDataRequest(
            Sel(false, false, comps: Tree(
                ("VEVENT", Sel(false, false, Props("SUMMARY"), Tree(
                    ("VALARM", Sel(false, false, Props("ACTION")))))))),
            null);

        var result = _formatter.FormatCalendar(EventAndTodo, request);
        Assert.Contains("BEGIN:VALARM", result);
        Assert.Contains("ACTION:DISPLAY", result);
        Assert.DoesNotContain("TRIGGER", result);   // nested VALARM prop not selected → dropped
        Assert.DoesNotContain("DESCRIPTION", result);
        Assert.DoesNotContain("VTODO", result);      // VTODO not listed at the VCALENDAR level → dropped
    }

    [Fact]
    public void Same_subcomponent_under_different_parents_uses_its_own_selection()
    {
        // VEVENT's VALARM: ACTION only. VTODO: keep all its sub-components (so its VALARM stays whole).
        var request = new CalendarDataRequest(
            Sel(false, false, comps: Tree(
                ("VEVENT", Sel(false, false, Props("SUMMARY"), Tree(("VALARM", Sel(false, false, Props("ACTION")))))),
                ("VTODO", Sel(false, true, Props("SUMMARY"))))),
            null);

        var result = _formatter.FormatCalendar(EventAndTodo, request);
        var split = result.IndexOf("BEGIN:VTODO", StringComparison.Ordinal);
        var eventPart = result[..split];
        var todoPart = result[split..];

        Assert.Contains("ACTION:DISPLAY", eventPart);
        Assert.DoesNotContain("TRIGGER", eventPart);  // the VEVENT alarm is trimmed to ACTION
        Assert.Contains("TRIGGER:-PT5M", todoPart);    // the VTODO alarm is kept whole (a different selection)
    }

    [Fact]
    public void Expand_produces_one_vevent_per_occurrence_without_rrule()
    {
        var request = new CalendarDataRequest(
            Root: null,
            new ExpandWindow(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        var result = _formatter.FormatCalendar(Weekly, request);
        Assert.Equal(3, Regex.Matches(result, "BEGIN:VEVENT").Count);
        Assert.Equal(3, Regex.Matches(result, "RECURRENCE-ID").Count);
        Assert.DoesNotContain("RRULE", result);
    }

    private const string WeeklyWithOverrides =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:w1\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\nSUMMARY:Series\r\nRRULE:FREQ=WEEKLY;COUNT=10\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:w1\r\nRECURRENCE-ID:20260708T090000Z\r\nDTSTART:20260708T140000Z\r\nDTEND:20260708T150000Z\r\nSUMMARY:MovedInRange\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:w1\r\nRECURRENCE-ID:20260805T090000Z\r\nDTSTART:20260805T140000Z\r\nDTEND:20260805T150000Z\r\nSUMMARY:MovedOutOfRange\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [Fact]
    public void Limit_recurrence_set_keeps_the_master_and_only_in_range_overrides()
    {
        var request = new CalendarDataRequest(
            Root: null,
            Expand: null,
            Limit: new RecurrenceLimit(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));

        var result = _formatter.FormatCalendar(WeeklyWithOverrides, request);

        Assert.Contains("RRULE", result);
        Assert.Contains("FREQ=WEEKLY", result);
        Assert.Contains("MovedInRange", result);
        Assert.DoesNotContain("MovedOutOfRange", result);
        Assert.Equal(2, Regex.Matches(result, "BEGIN:VEVENT").Count);
    }

    [Fact]
    public void Expand_takes_precedence_over_limit_recurrence_set()
    {
        var request = new CalendarDataRequest(
            Root: null,
            Expand: new ExpandWindow(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
            Limit: new RecurrenceLimit(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));

        var result = _formatter.FormatCalendar(Weekly, request);

        Assert.DoesNotContain("RRULE", result);
        Assert.Equal(3, Regex.Matches(result, "BEGIN:VEVENT").Count);
    }
}
