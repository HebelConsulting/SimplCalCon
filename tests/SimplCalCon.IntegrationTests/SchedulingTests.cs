using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Attendees + free/busy: REST, CalDAV free-busy-query REPORT, and the RFC 6638 schedule-outbox (ADR 0030).</summary>
public sealed class SchedulingTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    [Fact]
    public async Task Rest_event_round_trips_organizer_and_attendees()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);

        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Planning",
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
            organizer = "alice@demo.test",
            attendees = new[] { new { address = "bob@demo.test", commonName = "Bob" } },
        });
        var id = (await Body(created)).GetProperty("id").GetGuid();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{id}");
        var attendees = fetched.GetProperty("attendees").EnumerateArray().ToList();
        Assert.Contains(attendees, a => a.GetProperty("isOrganizer").GetBoolean() && a.GetProperty("address").GetString() == "mailto:alice@demo.test");
        Assert.Contains(attendees, a => !a.GetProperty("isOrganizer").GetBoolean()
            && a.GetProperty("address").GetString() == "mailto:bob@demo.test"
            && a.GetProperty("participationStatus").GetString() == "NeedsAction");
    }

    [Fact]
    public async Task Dav_put_extracts_attendees_into_the_index()
    {
        var (client, userId, email) = await DavTestUser.CreateDetailedAsync(factory, "sched");
        var calendar = await CreateDavCalendarAsync(client, userId);
        var uid = $"evt-{Guid.NewGuid():N}";

        var put = await Put(client, $"/dav/calendars/{userId}/{calendar}/{uid}.ics", $"""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//EN
            BEGIN:VEVENT
            UID:{uid}
            SUMMARY:Sync
            DTSTART:20260715T090000Z
            DTEND:20260715T100000Z
            ORGANIZER:mailto:{email}
            ATTENDEE;PARTSTAT=ACCEPTED:mailto:helper@demo.test
            END:VEVENT
            END:VCALENDAR
            """);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var rows = await db.EventAttendees
            .Where(a => a.Object!.Uid == uid)
            .ToListAsync();
        Assert.Contains(rows, a => a.IsOrganizer);
        Assert.Contains(rows, a => !a.IsOrganizer && a.NormalizedAddress == "MAILTO:HELPER@DEMO.TEST");
    }

    [Fact]
    public async Task Rest_free_busy_reflects_events()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        // A unique far-future date so other tests' events in the shared demo account can't merge into this window.
        await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Busy",
            startUtc = new DateTime(2031, 3, 10, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2031, 3, 10, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        });

        var response = await client.GetFromJsonAsync<JsonElement>(
            $"/api/free-busy?address={AuthWebApplicationFactory.DemoAdminEmail}&fromUtc=2031-03-10T00:00:00Z&toUtc=2031-03-11T00:00:00Z");

        Assert.True(response.GetProperty("resolved").GetBoolean());
        var busy = response.GetProperty("busy").EnumerateArray().ToList();
        Assert.Contains(busy, b =>
            b.GetProperty("startUtc").GetDateTime().ToUniversalTime() <= new DateTime(2031, 3, 10, 9, 0, 0, DateTimeKind.Utc)
            && b.GetProperty("endUtc").GetDateTime().ToUniversalTime() >= new DateTime(2031, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Caldav_free_busy_query_returns_vfreebusy()
    {
        var (client, userId, _) = await DavTestUser.CreateDetailedAsync(factory, "fb");
        var calendar = await CreateDavCalendarAsync(client, userId);
        var uid = $"evt-{Guid.NewGuid():N}";
        await Put(client, $"/dav/calendars/{userId}/{calendar}/{uid}.ics", SimpleEvent(uid));

        var report = await Report(client, $"/dav/calendars/{userId}/{calendar}", """
            <C:free-busy-query xmlns:C="urn:ietf:params:xml:ns:caldav">
              <C:time-range start="20260715T000000Z" end="20260716T000000Z"/>
            </C:free-busy-query>
            """);

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var body = await report.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VFREEBUSY", body);
        Assert.Contains("FREEBUSY:20260715T090000Z/20260715T100000Z", body);
    }

    [Fact]
    public async Task Principal_advertises_scheduling_and_outbox_answers_free_busy()
    {
        var (client, userId, email) = await DavTestUser.CreateDetailedAsync(factory, "out");
        var calendar = await CreateDavCalendarAsync(client, userId);
        var uid = $"evt-{Guid.NewGuid():N}";
        await Put(client, $"/dav/calendars/{userId}/{calendar}/{uid}.ics", SimpleEvent(uid));

        // Principal advertises the RFC 6638 discovery props.
        var principal = await Report(client, $"/dav/principals/{userId}/", method: "PROPFIND", depth: 0, body: """
            <propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <prop><c:calendar-user-address-set/><c:schedule-outbox-URL/></prop>
            </propfind>
            """);
        var pdoc = XDocument.Parse(await principal.Content.ReadAsStringAsync());
        Assert.Contains(pdoc.Descendants(CalDav + "calendar-user-address-set").Descendants(Dav + "href"), h => h.Value == $"mailto:{email}");
        Assert.Equal($"/dav/calendars/{userId}/outbox/",
            pdoc.Descendants(CalDav + "schedule-outbox-URL").Descendants(Dav + "href").First().Value);

        // Outbox free-busy POST returns a schedule-response with the attendee's busy time.
        var outbox = await Post(client, $"/dav/calendars/{userId}/outbox", $"""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//EN
            METHOD:REQUEST
            BEGIN:VFREEBUSY
            UID:fb-{Guid.NewGuid():N}
            DTSTART:20260715T000000Z
            DTEND:20260716T000000Z
            ORGANIZER:mailto:{email}
            ATTENDEE:mailto:{email}
            END:VFREEBUSY
            END:VCALENDAR
            """);
        Assert.Equal(HttpStatusCode.OK, outbox.StatusCode);
        var odoc = XDocument.Parse(await outbox.Content.ReadAsStringAsync());
        Assert.Contains(odoc.Descendants(CalDav + "response"), r =>
            r.Element(CalDav + "recipient")?.Element(Dav + "href")?.Value == $"mailto:{email}"
            && (r.Element(CalDav + "calendar-data")?.Value.Contains("FREEBUSY:20260715T090000Z/20260715T100000Z") ?? false));
    }

    private static string SimpleEvent(string uid) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:{uid}
        SUMMARY:Busy
        DTSTART:20260715T090000Z
        DTEND:20260715T100000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private static async Task<string> CreateDavCalendarAsync(HttpClient client, Guid userId)
    {
        var name = $"c{Guid.NewGuid():N}";
        var mk = await client.SendAsync(new HttpRequestMessage(new HttpMethod("MKCALENDAR"), $"/dav/calendars/{userId}/{name}"));
        Assert.Equal(HttpStatusCode.Created, mk.StatusCode);
        return name;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string url, string ics)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StringContent(ics, Encoding.UTF8, "text/calendar") };
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string ics)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(ics, Encoding.UTF8, "text/calendar") };
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Report(
        HttpClient client, string url, string? body, string method = "REPORT", int? depth = null)
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

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Report(HttpClient client, string url, string body) =>
        Report(client, url, body, "REPORT", null);

    [Fact]
    public async Task Rest_invitation_is_listed_then_accepted_and_added_to_calendar()
    {
        var client = await BearerClientAsync();
        var uid = $"inv-{Guid.NewGuid():N}";

        // Seed a REQUEST into the demo admin's schedule-inbox (as an organizer's invite would deliver).
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
            var admin = await db.Users.FirstAsync(u => u.NormalizedEmail == AuthWebApplicationFactory.DemoAdminEmail.ToUpperInvariant());
            userId = admin.Id;
            var inboxes = scope.ServiceProvider.GetRequiredService<IScheduleInboxRepository>();
            var inbox = await inboxes.EnsureInboxAsync(userId, admin.TenantId!.Value, default);
            await inboxes.DeliverAsync(inbox.Id, RequestBlob(uid, AuthWebApplicationFactory.DemoAdminEmail), "REQUEST", default);
        }

        // It appears over REST.
        var list = await client.GetFromJsonAsync<JsonElement>("/api/invitations");
        var mine = list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("uid").GetString() == uid);
        Assert.Equal("Team Offsite", mine.GetProperty("summary").GetString());
        var resourceName = mine.GetProperty("resourceName").GetString();

        // Accept it.
        var respond = await client.PostAsJsonAsync("/api/invitations/respond", new { resourceName, response = "accepted" });
        Assert.Equal(HttpStatusCode.NoContent, respond.StatusCode);

        // Drained from the inbox, and now in the user's calendar.
        var after = await client.GetFromJsonAsync<JsonElement>("/api/invitations");
        Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(), i => i.GetProperty("uid").GetString() == uid);

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var exists = await context.CalendarObjects.AnyAsync(o => o.Uid == uid && !o.IsDeleted
            && context.Calendars.Any(c => c.Id == o.CollectionId && c.OwnerId == userId));
        Assert.True(exists);
    }

    private static string RequestBlob(string uid, string attendeeEmail) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        METHOD:REQUEST
        BEGIN:VEVENT
        UID:{uid}
        SUMMARY:Team Offsite
        DTSTART:20260801T090000Z
        DTEND:20260801T100000Z
        ORGANIZER:mailto:organizer@demo.test
        ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:{attendeeEmail}
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public async Task Tenant_email_settings_round_trip_and_never_return_the_password()
    {
        var client = await BearerClientAsync();

        var put = await client.PutAsJsonAsync("/api/admin/email-settings", new
        {
            enabled = true, host = "smtp.example.test", port = 587, useStartTls = true,
            username = "mailer", newPassword = "s3cret", fromAddress = "cal@example.test", fromName = "Calendar",
        });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var got = await client.GetFromJsonAsync<JsonElement>("/api/admin/email-settings");
        Assert.True(got.GetProperty("enabled").GetBoolean());
        Assert.Equal("smtp.example.test", got.GetProperty("host").GetString());
        Assert.True(got.GetProperty("hasPassword").GetBoolean());
        Assert.False(got.TryGetProperty("password", out _)); // the password value is never returned
    }

    [Fact]
    public async Task External_attendee_receives_an_imip_request_email()
    {
        var client = await BearerClientAsync();
        await client.PutAsJsonAsync("/api/admin/email-settings", new
        {
            enabled = true, host = "smtp.example.test", port = 25, useStartTls = false,
            username = (string?)null, newPassword = (string?)null, fromAddress = "cal@example.test", fromName = "Calendar",
        });

        var calendarId = await CreateCalendarAsync(client);
        var summary = $"Ext-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary,
            startUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
            organizer = AuthWebApplicationFactory.DemoAdminEmail,
            attendees = new[] { new { address = "outsider@external.example", commonName = "Outsider" } },
        });

        var sent = factory.EmailSender.Sent.SingleOrDefault(m => m.Mail.CalendarBody.Contains(summary));
        Assert.NotNull(sent.Mail);
        Assert.Equal("REQUEST", sent.Mail.Method);
        Assert.Equal("outsider@external.example", sent.Mail.To);
    }

    private static async Task<Guid> CreateCalendarAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" }))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> BearerClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
