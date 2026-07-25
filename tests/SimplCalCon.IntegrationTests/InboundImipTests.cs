using System.Net;
using System.Text;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Inbound iMIP ingestion over the REST endpoint (ADR 0056): auth, REQUEST → inbox, REPLY → auto-apply.</summary>
public sealed class InboundImipTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Endpoint_rejects_a_wrong_or_missing_key()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/inbound-imip", new StringContent("x", Encoding.UTF8, "message/rfc822"))).StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/inbound-imip")
        {
            Content = new StringContent("x", Encoding.UTF8, "message/rfc822"),
        };
        wrong.Headers.Add("X-Inbound-Key", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);
    }

    [Fact]
    public async Task Request_is_delivered_to_the_local_attendees_inbox()
    {
        var att = await DavTestUser.CreateDetailedAsync(factory, "inb-att");
        var mime = Mime("REQUEST",
            $"BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:inb-req-{Guid.NewGuid():N}\r\n" +
            $"DTSTART:20260801T090000Z\r\nSUMMARY:External Meeting\r\nORGANIZER:mailto:organizer@external.test\r\n" +
            $"ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:{att.Email}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        var response = await PostInboundAsync(mime);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("DeliveredToInbox", (await Body(response)).GetProperty("outcome").GetString());

        var inbox = await InboxDataAsync(att.Client, att.UserId);
        Assert.Contains("METHOD:REQUEST", inbox);
        Assert.Contains("External Meeting", inbox);
    }

    [Fact]
    public async Task Reply_applies_the_external_attendees_partstat_to_the_organizer_copy()
    {
        // A local organizer (DAV) holds an event inviting an external attendee — a known UID we control.
        var org = await DavTestUser.CreateDetailedAsync(factory, "inb-org");
        var cal = $"cal-{Guid.NewGuid():N}";
        await DavSend(org.Client, "MKCALENDAR", $"/dav/calendars/{org.UserId}/{cal}/");
        var uid = $"inb-reply-{Guid.NewGuid():N}";
        await DavSend(org.Client, "PUT", $"/dav/calendars/{org.UserId}/{cal}/{uid}.ics",
            $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART:20260801T090000Z\r\nSUMMARY:Sync\r\n" +
            $"ORGANIZER:mailto:{org.Email}\r\nATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:vendor@external.test\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
            "text/calendar");

        var mime = Mime("REPLY",
            $"BEGIN:VCALENDAR\r\nMETHOD:REPLY\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n" +
            $"ORGANIZER:mailto:{org.Email}\r\nATTENDEE;PARTSTAT=ACCEPTED:mailto:vendor@external.test\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        Assert.Equal(HttpStatusCode.Accepted, (await PostInboundAsync(mime)).StatusCode);

        // The organizer's copy now shows the external attendee ACCEPTED.
        var stored = await (await org.Client.GetAsync($"/dav/calendars/{org.UserId}/{cal}/{uid}.ics")).Content.ReadAsStringAsync();
        Assert.Contains("PARTSTAT=ACCEPTED", stored);
    }

    private async Task<HttpResponseMessage> PostInboundAsync(string mime)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inbound-imip")
        {
            Content = new StringContent(mime, Encoding.UTF8, "message/rfc822"),
        };
        request.Headers.Add("X-Inbound-Key", AuthWebApplicationFactory.InboundApiKey);
        return await client.SendAsync(request);
    }

    private static string Mime(string method, string ics) =>
        "From: organizer@external.test\r\n" +
        "To: recipient@simplcalcon.test\r\n" +
        $"Subject: iMIP {method}\r\n" +
        "MIME-Version: 1.0\r\n" +
        $"Content-Type: text/calendar; method={method}; charset=UTF-8\r\n\r\n" +
        ics;

    private static async Task<HttpResponseMessage> DavSend(HttpClient client, string method, string url, string? content = null, string? contentType = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "text/plain");
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> InboxDataAsync(HttpClient client, Guid userId)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), $"/dav/calendars/{userId}/inbox")
        {
            Content = new StringContent(
                """<propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><prop><c:calendar-data/></prop></propfind>""",
                Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");
        return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
