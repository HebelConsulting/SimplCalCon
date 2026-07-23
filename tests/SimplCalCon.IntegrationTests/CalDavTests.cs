using System.Net;
using System.Text;
using System.Xml.Linq;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class CalDavTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    private static string Event(string uid, string summary, string dtStart, string? rrule = null) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:{uid}
        SUMMARY:{summary}
        DTSTART:{dtStart}
        DTEND:{dtStart[..9]}100000Z
        {(rrule is null ? "" : $"RRULE:{rrule}\n")}END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public async Task Principal_advertises_calendar_home_set()
    {
        var (client, userId) = await DavClientAsync();

        var response = await SendAsync(client, "PROPFIND", $"/dav/principals/{userId}/", depth: 0, body: """
            <propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <prop><c:calendar-home-set/></prop>
            </propfind>
            """);

        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"/dav/calendars/{userId}/",
            doc.Descendants(CalDav + "calendar-home-set").Descendants(Dav + "href").First().Value);
    }

    [Fact]
    public async Task Home_auto_provisions_a_default_calendar()
    {
        var (client, userId) = await DavClientAsync();

        var home = await SendAsync(client, "PROPFIND", $"/dav/calendars/{userId}/", depth: 1, body: """
            <propfind xmlns="DAV:"><prop><resourcetype/><displayname/></prop></propfind>
            """);

        var doc = XDocument.Parse(await home.Content.ReadAsStringAsync());
        Assert.Contains(doc.Descendants(Dav + "href"), h => h.Value == $"/dav/calendars/{userId}/calendar/");
    }

    [Fact]
    public async Task Put_get_and_time_range_query_with_recurrence()
    {
        var (client, userId) = await DavClientAsync();
        var cal = await CreateCalendarAsync(client, userId);
        var basePath = $"/dav/calendars/{userId}/{cal}";

        var created = await SendAsync(client, "PUT", $"{basePath}/in.ics", content: Event("in@t", "In", "20260715T090000Z"), contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await SendAsync(client, "PUT", $"{basePath}/out.ics", content: Event("out@t", "Out", "20250101T090000Z"), contentType: "text/calendar");
        await SendAsync(client, "PUT", $"{basePath}/rec.ics", content: Event("rec@t", "Weekly", "20260701T090000Z", "FREQ=WEEKLY;COUNT=4"), contentType: "text/calendar");

        var fetched = await client.GetAsync($"{basePath}/in.ics");
        Assert.Contains("SUMMARY:In", await fetched.Content.ReadAsStringAsync());

        // Query July 10–20: the in-range single event and the recurring one (Jul 15 occurrence) match; the 2025 event doesn't.
        var query = await SendAsync(client, "REPORT", $"{basePath}/", body: """
            <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:prop><d:getetag/><c:calendar-data/></d:prop>
              <c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT">
                <c:time-range start="20260710T000000Z" end="20260720T000000Z"/>
              </c:comp-filter></c:comp-filter></c:filter>
            </c:calendar-query>
            """);

        var doc = XDocument.Parse(await query.Content.ReadAsStringAsync());
        var hrefs = doc.Descendants(Dav + "href").Select(h => h.Value).ToList();
        Assert.Contains(hrefs, h => h.EndsWith("/in.ics"));
        Assert.Contains(hrefs, h => h.EndsWith("/rec.ics"));
        Assert.DoesNotContain(hrefs, h => h.EndsWith("/out.ics"));
    }

    [Fact]
    public async Task Sync_collection_reports_changes_and_removals()
    {
        var (client, userId) = await DavClientAsync();
        var cal = await CreateCalendarAsync(client, userId);
        var basePath = $"/dav/calendars/{userId}/{cal}";

        await SendAsync(client, "PUT", $"{basePath}/a.ics", content: Event("a@t", "A", "20260201T090000Z"), contentType: "text/calendar");
        var initial = await SendAsync(client, "REPORT", $"{basePath}/", body: SyncBody(null));
        var token = XDocument.Parse(await initial.Content.ReadAsStringAsync()).Descendants(Dav + "sync-token").First().Value;

        await SendAsync(client, "PUT", $"{basePath}/b.ics", content: Event("b@t", "B", "20260202T090000Z"), contentType: "text/calendar");
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{basePath}/a.ics"));

        var delta = XDocument.Parse(await (await SendAsync(client, "REPORT", $"{basePath}/", body: SyncBody(token))).Content.ReadAsStringAsync());
        var responses = delta.Descendants(Dav + "response").ToList();
        Assert.Contains(responses, r => r.Element(Dav + "href")!.Value.EndsWith("/b.ics"));
        Assert.Contains(responses, r => r.Element(Dav + "href")!.Value.EndsWith("/a.ics") && r.Element(Dav + "status")!.Value.Contains("404"));
    }

    [Fact]
    public async Task Mkcalendar_creates_a_calendar()
    {
        var (client, userId) = await DavClientAsync();
        var name = $"c{Guid.NewGuid():N}";

        var mk = await client.SendAsync(new HttpRequestMessage(new HttpMethod("MKCALENDAR"), $"/dav/calendars/{userId}/{name}"));
        Assert.Equal(HttpStatusCode.Created, mk.StatusCode);

        var home = await SendAsync(client, "PROPFIND", $"/dav/calendars/{userId}/", depth: 1, body: "<propfind xmlns=\"DAV:\"><prop><resourcetype/></prop></propfind>");
        var doc = XDocument.Parse(await home.Content.ReadAsStringAsync());
        Assert.Contains(doc.Descendants(Dav + "href"), h => h.Value == $"/dav/calendars/{userId}/{name}/");
    }

    private static string SyncBody(string? token) =>
        $"<sync-collection xmlns=\"DAV:\"><sync-token>{token}</sync-token><sync-level>1</sync-level><prop><getetag/></prop></sync-collection>";

    private async Task<string> CreateCalendarAsync(HttpClient client, Guid userId)
    {
        var name = $"c{Guid.NewGuid():N}";
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod("MKCALENDAR"), $"/dav/calendars/{userId}/{name}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return name;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string url, string? body = null, string? content = null, string? contentType = null, int? depth = null)
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

    // A fresh, isolated user per test so provisioning/listing don't interfere across tests.
    private async Task<(HttpClient Client, Guid UserId)> DavClientAsync() =>
        await DavTestUser.CreateAsync(factory, "caldav");
}
