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
            new Dictionary<string, DavCompSelection>
            {
                ["VCALENDAR"] = new(AllProps: false, AllComps: true, new HashSet<string>()),
                ["VEVENT"] = new(AllProps: false, AllComps: false, new HashSet<string> { "SUMMARY" }),
            },
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
        var result = _formatter.FormatContact(Vcard, new AddressDataRequest(new HashSet<string> { "FN" }));
        Assert.Contains("FN:Jane Doe", result);
        Assert.Contains("UID:c1", result);
        Assert.DoesNotContain("EMAIL", result);
        Assert.DoesNotContain("TEL", result);
    }

    [Fact]
    public void All_props_keeps_every_property_of_the_component()
    {
        var request = new CalendarDataRequest(
            new Dictionary<string, DavCompSelection>
            {
                ["VCALENDAR"] = new(false, true, new HashSet<string>()),
                ["VEVENT"] = new(AllProps: true, AllComps: false, new HashSet<string>()),
            },
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
            new Dictionary<string, DavCompSelection>
            {
                ["VCALENDAR"] = new(false, true, new HashSet<string>()),
                ["VEVENT"] = new(false, false, new HashSet<string> { "SUMMARY" }),
            },
            null);

        var result = _formatter.FormatCalendar(withTz, request);
        Assert.Contains("VTIMEZONE", result);
        Assert.Contains("TZID:Europe/Zurich", result);
        Assert.DoesNotContain("DESCRIPTION", result);
    }

    [Fact]
    public void Expand_produces_one_vevent_per_occurrence_without_rrule()
    {
        var request = new CalendarDataRequest(
            new Dictionary<string, DavCompSelection>(),
            new ExpandWindow(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        var result = _formatter.FormatCalendar(Weekly, request);
        Assert.Equal(3, Regex.Matches(result, "BEGIN:VEVENT").Count);
        Assert.Equal(3, Regex.Matches(result, "RECURRENCE-ID").Count);
        Assert.DoesNotContain("RRULE", result);
    }
}
