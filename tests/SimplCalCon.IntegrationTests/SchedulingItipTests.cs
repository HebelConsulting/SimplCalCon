using System.Net;
using System.Text;
using System.Xml.Linq;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>RFC 6638 automatic scheduling round-trip: REQUEST → REPLY (auto-applied) → CANCEL (ADR 0031).</summary>
public sealed class SchedulingItipTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    [Fact]
    public async Task Organizer_put_delivers_a_request_to_the_attendee_inbox()
    {
        var org = await DavTestUser.CreateDetailedAsync(factory, "org");
        var att = await DavTestUser.CreateDetailedAsync(factory, "att");
        var cal = await MkcalendarAsync(org.Client, org.UserId);
        var uid = $"evt-{Guid.NewGuid():N}";

        var put = await Put(org.Client, $"/dav/calendars/{org.UserId}/{cal}/{uid}.ics",
            EventWithAttendee(uid, org.Email, att.Email, "NEEDS-ACTION"));
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        var inbox = await InboxDataAsync(att.Client, att.UserId);
        Assert.Contains("METHOD:REQUEST", inbox);
        Assert.Contains(uid, inbox);
    }

    [Fact]
    public async Task Attendee_reply_delivers_a_reply_and_auto_applies_to_the_organizer_event()
    {
        var org = await DavTestUser.CreateDetailedAsync(factory, "org");
        var att = await DavTestUser.CreateDetailedAsync(factory, "att");
        var orgCal = await MkcalendarAsync(org.Client, org.UserId);
        var attCal = await MkcalendarAsync(att.Client, att.UserId);
        var uid = $"evt-{Guid.NewGuid():N}";

        await Put(org.Client, $"/dav/calendars/{org.UserId}/{orgCal}/{uid}.ics",
            EventWithAttendee(uid, org.Email, att.Email, "NEEDS-ACTION"));

        // Attendee accepts by storing their copy with PARTSTAT=ACCEPTED.
        var reply = await Put(att.Client, $"/dav/calendars/{att.UserId}/{attCal}/{uid}.ics",
            EventWithAttendee(uid, org.Email, att.Email, "ACCEPTED"));
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);

        // A REPLY reached the organizer's inbox…
        Assert.Contains("METHOD:REPLY", await InboxDataAsync(org.Client, org.UserId));

        // …and the organizer's own copy now shows the attendee ACCEPTED (auto-apply).
        var organizerEvent = await (await Get(org.Client, $"/dav/calendars/{org.UserId}/{orgCal}/{uid}.ics"))
            .Content.ReadAsStringAsync();
        Assert.Contains("PARTSTAT=ACCEPTED", organizerEvent);
    }

    [Fact]
    public async Task Organizer_delete_delivers_a_cancel_to_the_attendee_inbox()
    {
        var org = await DavTestUser.CreateDetailedAsync(factory, "org");
        var att = await DavTestUser.CreateDetailedAsync(factory, "att");
        var cal = await MkcalendarAsync(org.Client, org.UserId);
        var uid = $"evt-{Guid.NewGuid():N}";
        await Put(org.Client, $"/dav/calendars/{org.UserId}/{cal}/{uid}.ics",
            EventWithAttendee(uid, org.Email, att.Email, "NEEDS-ACTION"));

        var delete = await org.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/dav/calendars/{org.UserId}/{cal}/{uid}.ics"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var inbox = await InboxDataAsync(att.Client, att.UserId);
        Assert.Contains("METHOD:CANCEL", inbox);
    }

    [Fact]
    public async Task Attendee_can_drain_an_inbox_message()
    {
        var org = await DavTestUser.CreateDetailedAsync(factory, "org");
        var att = await DavTestUser.CreateDetailedAsync(factory, "att");
        var cal = await MkcalendarAsync(org.Client, org.UserId);
        var uid = $"evt-{Guid.NewGuid():N}";
        await Put(org.Client, $"/dav/calendars/{org.UserId}/{cal}/{uid}.ics",
            EventWithAttendee(uid, org.Email, att.Email, "NEEDS-ACTION"));

        // Find the delivered message href, GET it, DELETE it, then confirm it's gone.
        var href = await FirstInboxMessageHrefAsync(att.Client, att.UserId);
        Assert.NotNull(href);
        Assert.Equal(HttpStatusCode.OK, (await Get(att.Client, href!)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await att.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, href))).StatusCode);
        Assert.DoesNotContain("METHOD:REQUEST", await InboxDataAsync(att.Client, att.UserId));
    }

    private static string EventWithAttendee(string uid, string organizer, string attendee, string partStat) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:{uid}
        SUMMARY:Meeting
        DTSTART:20260901T090000Z
        DTEND:20260901T100000Z
        ORGANIZER:mailto:{organizer}
        ATTENDEE;PARTSTAT={partStat}:mailto:{attendee}
        END:VEVENT
        END:VCALENDAR
        """;

    private static async Task<string> MkcalendarAsync(HttpClient client, Guid userId)
    {
        var name = $"c{Guid.NewGuid():N}";
        var mk = await client.SendAsync(new HttpRequestMessage(new HttpMethod("MKCALENDAR"), $"/dav/calendars/{userId}/{name}"));
        Assert.Equal(HttpStatusCode.Created, mk.StatusCode);
        return name;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string url, string ics) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(ics, Encoding.UTF8, "text/calendar"),
        });

    private static Task<HttpResponseMessage> Get(HttpClient client, string url) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

    private static async Task<string> InboxDataAsync(HttpClient client, Guid userId) =>
        await (await InboxPropfindAsync(client, userId)).Content.ReadAsStringAsync();

    private static Task<HttpResponseMessage> InboxPropfindAsync(HttpClient client, Guid userId)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), $"/dav/calendars/{userId}/inbox")
        {
            Content = new StringContent(
                """<propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><prop><getcontenttype/><c:calendar-data/></prop></propfind>""",
                Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");
        return client.SendAsync(request);
    }

    private static async Task<string?> FirstInboxMessageHrefAsync(HttpClient client, Guid userId)
    {
        var doc = XDocument.Parse(await (await InboxPropfindAsync(client, userId)).Content.ReadAsStringAsync());
        return doc.Descendants(Dav + "href")
            .Select(h => h.Value)
            .FirstOrDefault(h => h.Contains("/inbox/") && h.EndsWith(".ics", StringComparison.Ordinal));
    }
}
