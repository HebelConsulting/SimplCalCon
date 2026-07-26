using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>DAV depth (ADR 0054): partial calendar-data/address-data, param-filter, and calendar-data expand.</summary>
public sealed class DavDepthTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Multiget_returns_only_the_requested_calendar_properties()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-cal");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        await Send(client, "PUT", $"{path}/e.ics", content: FullEvent("full@t"), contentType: "text/calendar");

        var report = await Send(client, "REPORT", $"{path}/", body:
            $"""
            <C:calendar-multiget xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop><C:calendar-data>
                <C:comp name="VCALENDAR"><C:prop name="VERSION"/>
                  <C:comp name="VEVENT"><C:prop name="UID"/><C:prop name="SUMMARY"/></C:comp>
                </C:comp>
              </C:calendar-data></D:prop>
              <D:href>{path}/e.ics</D:href>
            </C:calendar-multiget>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:", xml);
        Assert.Contains("UID:", xml);
        Assert.DoesNotContain("DESCRIPTION:", xml);
        Assert.DoesNotContain("LOCATION:", xml);
    }

    [Fact]
    public async Task Multiget_returns_only_the_requested_vcard_properties()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-card");
        var book = await ContactsBookAsync(client, userId);
        var path = $"/dav/addressbooks/{userId}/{book}";
        await Send(client, "PUT", $"{path}/c.vcf", content: FullCard("card@t"), contentType: "text/vcard");

        var report = await Send(client, "REPORT", $"{path}/", body:
            $"""
            <C:addressbook-multiget xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:carddav">
              <D:prop><C:address-data><C:prop name="FN"/></C:address-data></D:prop>
              <D:href>{path}/c.vcf</D:href>
            </C:addressbook-multiget>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("FN:", xml);
        Assert.DoesNotContain("EMAIL", xml);
        Assert.DoesNotContain("TEL", xml);
    }

    [Fact]
    public async Task Calendar_query_param_filter_matches_on_a_property_parameter()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-param");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        await Send(client, "PUT", $"{path}/pending.ics", content: EventWithAttendee("pending@t", "NEEDS-ACTION"), contentType: "text/calendar");
        await Send(client, "PUT", $"{path}/accepted.ics", content: EventWithAttendee("accepted@t", "ACCEPTED"), contentType: "text/calendar");

        var report = await Send(client, "REPORT", $"{path}/", body:
            """
            <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop><D:getetag/></D:prop>
              <C:filter><C:comp-filter name="VCALENDAR"><C:comp-filter name="VEVENT">
                <C:prop-filter name="ATTENDEE">
                  <C:param-filter name="PARTSTAT"><C:text-match>NEEDS-ACTION</C:text-match></C:param-filter>
                </C:prop-filter>
              </C:comp-filter></C:comp-filter></C:filter>
            </C:calendar-query>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("pending.ics", xml);
        Assert.DoesNotContain("accepted.ics", xml);
    }

    [Fact]
    public async Task Calendar_query_expand_returns_one_vevent_per_occurrence()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-expand");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        await Send(client, "PUT", $"{path}/w.ics", content: WeeklyEvent("weekly@t"), contentType: "text/calendar");

        var report = await Send(client, "REPORT", $"{path}/", body:
            """
            <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop><C:calendar-data>
                <C:expand start="20260901T000000Z" end="20261001T000000Z"/>
              </C:calendar-data></D:prop>
              <C:filter><C:comp-filter name="VCALENDAR"><C:comp-filter name="VEVENT">
                <C:time-range start="20260901T000000Z" end="20261001T000000Z"/>
              </C:comp-filter></C:comp-filter></C:filter>
            </C:calendar-query>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Equal(3, Regex.Matches(xml, "BEGIN:VEVENT").Count);           // 3 weekly occurrences
        Assert.Equal(3, Regex.Matches(xml, "RECURRENCE-ID").Count);
        Assert.DoesNotContain("RRULE", xml);                                 // expansion drops the rule
    }

    [Fact]
    public async Task Multiget_limit_recurrence_set_keeps_master_and_only_in_range_overrides()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-limit");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        await Send(client, "PUT", $"{path}/s.ics", content: SeriesWithOverrides("s@t"), contentType: "text/calendar");

        var report = await Send(client, "REPORT", $"{path}/", body:
            $"""
            <C:calendar-multiget xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop><C:calendar-data>
                <C:limit-recurrence-set start="20260701T000000Z" end="20260715T000000Z"/>
              </C:calendar-data></D:prop>
              <D:href>{path}/s.ics</D:href>
            </C:calendar-multiget>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("FREQ=WEEKLY", xml);              // master RRULE preserved (not expanded)
        Assert.Contains("MovedInRange", xml);             // override overlapping the window
        Assert.DoesNotContain("MovedOutOfRange", xml);    // override outside the window
    }

    [Fact]
    public async Task Multiget_honors_a_nested_comp_selection()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-nest");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        const string blob =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:a@t\r\n" +
            "DTSTAMP:20260715T090000Z\r\nDTSTART:20260715T090000Z\r\nSUMMARY:Team\r\nDESCRIPTION:Notes\r\n" +
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        await Send(client, "PUT", $"{path}/a.ics", content: blob, contentType: "text/calendar");

        // Keep VEVENT's SUMMARY and, nested, only ACTION inside its VALARM (ADR 0073).
        var report = await Send(client, "REPORT", $"{path}/", body:
            $"""
            <C:calendar-multiget xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop><C:calendar-data>
                <C:comp name="VCALENDAR"><C:comp name="VEVENT">
                  <C:prop name="SUMMARY"/>
                  <C:comp name="VALARM"><C:prop name="ACTION"/></C:comp>
                </C:comp></C:comp>
              </C:calendar-data></D:prop>
              <D:href>{path}/a.ics</D:href>
            </C:calendar-multiget>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:Team", xml);
        Assert.Contains("ACTION:DISPLAY", xml);   // nested VALARM component kept
        Assert.DoesNotContain("DESCRIPTION", xml);
        Assert.DoesNotContain("TRIGGER", xml);     // VALARM property not selected → dropped
    }

    [Fact]
    public async Task Sync_collection_honors_a_calendar_data_prop_subset()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-sync-cal");
        var cal = await MkcalendarAsync(client, userId);
        var path = $"/dav/calendars/{userId}/{cal}";
        await Send(client, "PUT", $"{path}/e.ics", content: FullEvent("full@t"), contentType: "text/calendar");

        var report = await Send(client, "REPORT", $"{path}/", body:
            """
            <D:sync-collection xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:sync-token/>
              <D:prop>
                <D:getetag/>
                <C:calendar-data>
                  <C:comp name="VCALENDAR"><C:comp name="VEVENT"><C:prop name="SUMMARY"/></C:comp></C:comp>
                </C:calendar-data>
              </D:prop>
            </D:sync-collection>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("SUMMARY:Team", xml);
        Assert.DoesNotContain("DESCRIPTION", xml); // subset applied on sync, not just multiget/query (ADR 0070)
        Assert.DoesNotContain("LOCATION", xml);
    }

    [Fact]
    public async Task Sync_collection_honors_an_address_data_prop_subset()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "depth-sync-card");
        var book = await ContactsBookAsync(client, userId);
        var path = $"/dav/addressbooks/{userId}/{book}";
        await Send(client, "PUT", $"{path}/c.vcf", content: FullCard("c@t"), contentType: "text/vcard");

        var report = await Send(client, "REPORT", $"{path}/", body:
            """
            <D:sync-collection xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:carddav">
              <D:sync-token/>
              <D:prop>
                <D:getetag/>
                <C:address-data><C:prop name="FN"/></C:address-data>
              </D:prop>
            </D:sync-collection>
            """);

        var xml = await report.Content.ReadAsStringAsync();
        Assert.Contains("FN:", xml);
        Assert.DoesNotContain("EMAIL", xml);
        Assert.DoesNotContain("TEL", xml);
    }

    private static string SeriesWithOverrides(string uid) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\nSUMMARY:Series\r\nRRULE:FREQ=WEEKLY;COUNT=10\r\nEND:VEVENT\r\n" +
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nRECURRENCE-ID:20260708T090000Z\r\nDTSTART:20260708T140000Z\r\nDTEND:20260708T150000Z\r\nSUMMARY:MovedInRange\r\nEND:VEVENT\r\n" +
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nRECURRENCE-ID:20260805T090000Z\r\nDTSTART:20260805T140000Z\r\nDTEND:20260805T150000Z\r\nSUMMARY:MovedOutOfRange\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    private static async Task<string> MkcalendarAsync(HttpClient client, Guid userId)
    {
        var name = $"cal-{Guid.NewGuid():N}";
        await Send(client, "MKCALENDAR", $"/dav/calendars/{userId}/{name}/");
        return name;
    }

    private static async Task<string> ContactsBookAsync(HttpClient client, Guid userId)
    {
        // PROPFIND the home to auto-provision the default "contacts" address book.
        await Send(client, "PROPFIND", $"/dav/addressbooks/{userId}/", depth: 1,
            body: """<propfind xmlns="DAV:"><prop><resourcetype/></prop></propfind>""");
        return "contacts";
    }

    private static string FullEvent(string uid) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nDTSTAMP:20260715T090000Z\r\nDTSTART:20260715T090000Z\r\n" +
        "SUMMARY:Team\r\nDESCRIPTION:Notes\r\nLOCATION:Room\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string EventWithAttendee(string uid, string partStat) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nDTSTAMP:20260715T090000Z\r\nDTSTART:20260715T090000Z\r\nSUMMARY:Meet\r\n" +
        $"ATTENDEE;PARTSTAT={partStat}:mailto:a@t\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string WeeklyEvent(string uid) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nDTSTAMP:20260907T090000Z\r\nDTSTART:20260907T090000Z\r\nSUMMARY:Standup\r\n" +
        "RRULE:FREQ=WEEKLY;COUNT=3\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string FullCard(string uid) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:Jane Doe\r\nN:Doe;Jane;;;\r\n" +
        "EMAIL:jane@example.com\r\nTEL:+15551234567\r\nEND:VCARD\r\n";

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string method, string url, string? body = null, string? content = null,
        string? contentType = null, int? depth = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (depth is not null)
        {
            request.Headers.Add("Depth", depth.ToString());
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        }
        else if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "text/plain");
        }

        return await client.SendAsync(request);
    }
}
