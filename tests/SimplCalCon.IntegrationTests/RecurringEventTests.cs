using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Recurring-event editing: structured RRULE round-trip, grid expansion, custom-rule preservation (ADR 0050).</summary>
public sealed class RecurringEventTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Structured_recurrence_round_trips_and_expands_into_occurrences()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);

        // A weekly event, 4 occurrences from a Monday.
        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Standup",
            startUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc),
            isAllDay = false,
            recurrence = new { frequency = "WEEKLY", interval = 1, byDay = Array.Empty<string>(), count = 4 },
        });
        created.EnsureSuccessStatusCode();
        var id = (await Body(created)).GetProperty("id").GetGuid();

        // GET surfaces the structured, supported recurrence.
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{id}");
        Assert.True(fetched.GetProperty("isRecurring").GetBoolean());
        Assert.True(fetched.GetProperty("recurrenceSupported").GetBoolean());
        Assert.Equal("WEEKLY", fetched.GetProperty("recurrence").GetProperty("frequency").GetString());
        Assert.Equal(4, fetched.GetProperty("recurrence").GetProperty("count").GetInt32());

        // expand=true over the window yields one item per weekly occurrence, all sharing the master id.
        var expanded = await client.GetFromJsonAsync<JsonElement>(
            $"/api/calendars/{calendarId}/events?fromUtc=2026-09-01T00:00:00Z&toUtc=2026-10-05T00:00:00Z&expand=true");
        var items = expanded.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.Equal(id, i.GetProperty("id").GetGuid()));
        var starts = items.Select(i => i.GetProperty("startUtc").GetDateTime().Day).OrderBy(d => d).ToList();
        Assert.Equal([7, 14, 21, 28], starts);
    }

    [Fact]
    public async Task Custom_rule_is_shown_read_only_and_preserved_on_edit()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);

        // A rule beyond the structured editor (BYSETPOS) is stored verbatim.
        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Last Monday",
            startUtc = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
            recurrenceRule = "FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO",
        });
        created.EnsureSuccessStatusCode();
        var id = (await Body(created)).GetProperty("id").GetGuid();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{id}");
        Assert.True(fetched.GetProperty("isRecurring").GetBoolean());
        Assert.False(fetched.GetProperty("recurrenceSupported").GetBoolean());
        Assert.Equal("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO", fetched.GetProperty("recurrenceRule").GetString());

        // Editing the summary while echoing the raw rule preserves it (the client's custom-rule path).
        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/calendars/{calendarId}/events/{id}")
        {
            Content = JsonContent.Create(new
            {
                summary = "Last Monday (renamed)",
                startUtc = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc),
                endUtc = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
                isAllDay = false,
                recurrenceRule = "FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO",
            }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", "*");
        (await client.SendAsync(update)).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{id}");
        Assert.Equal("Last Monday (renamed)", after.GetProperty("summary").GetString());
        Assert.Equal("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO", after.GetProperty("recurrenceRule").GetString());
    }

    [Fact]
    public async Task Deleting_one_occurrence_excludes_it_via_exdate()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var id = await CreateWeeklyAsync(client, calendarId, count: 4);

        var occurrences = await ExpandAsync(client, calendarId);
        Assert.Equal(4, occurrences.Count);
        var secondSlot = occurrences[1].GetProperty("recurrenceId").GetDateTime();

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/calendars/{calendarId}/events/{id}/occurrences/{Basic(secondSlot)}?scope=this");
        delete.Headers.TryAddWithoutValidation("If-Match", "*");
        (await client.SendAsync(delete)).EnsureSuccessStatusCode();

        var after = await ExpandAsync(client, calendarId);
        Assert.Equal(3, after.Count);
        Assert.DoesNotContain(after, o => o.GetProperty("startUtc").GetDateTime() == secondSlot);
    }

    [Fact]
    public async Task Overriding_one_occurrence_changes_only_that_instance()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var id = await CreateWeeklyAsync(client, calendarId, count: 4);

        var occurrences = await ExpandAsync(client, calendarId);
        var thirdSlot = occurrences[2].GetProperty("recurrenceId").GetDateTime();

        using var put = new HttpRequestMessage(
            HttpMethod.Put, $"/api/calendars/{calendarId}/events/{id}/occurrences/{Basic(thirdSlot)}?scope=this")
        {
            Content = JsonContent.Create(new
            {
                summary = "Moved standup",
                startUtc = thirdSlot.AddHours(1),
                endUtc = thirdSlot.AddHours(1).AddMinutes(15),
                isAllDay = false,
            }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", "*");
        (await client.SendAsync(put)).EnsureSuccessStatusCode();

        var after = await ExpandAsync(client, calendarId);
        Assert.Equal(4, after.Count);
        Assert.Single(after, o => o.GetProperty("summary").GetString() == "Moved standup");
        Assert.Equal(3, after.Count(o => o.GetProperty("summary").GetString() == "Standup"));
    }

    [Fact]
    public async Task Deleting_this_and_following_truncates_the_series()
    {
        var client = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var id = await CreateWeeklyAsync(client, calendarId, count: 4);

        var occurrences = await ExpandAsync(client, calendarId);
        var thirdSlot = occurrences[2].GetProperty("recurrenceId").GetDateTime();

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/calendars/{calendarId}/events/{id}/occurrences/{Basic(thirdSlot)}?scope=following");
        delete.Headers.TryAddWithoutValidation("If-Match", "*");
        (await client.SendAsync(delete)).EnsureSuccessStatusCode();

        var after = await ExpandAsync(client, calendarId);
        Assert.Equal(2, after.Count); // the 3rd and 4th are gone
    }

    [Fact]
    public async Task Overriding_one_occurrence_notifies_the_attendee(){
        var att = await DavTestUser.CreateDetailedAsync(factory, "occ-att");
        var org = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(org);
        var id = await CreateWeeklyWithAttendeeAsync(org, calendarId, att.Email);

        var slot = (await ExpandAsync(org, calendarId))[1].GetProperty("recurrenceId").GetDateTime();
        using var put = new HttpRequestMessage(
            HttpMethod.Put, $"/api/calendars/{calendarId}/events/{id}/occurrences/{Basic(slot)}?scope=this")
        {
            Content = JsonContent.Create(new
            {
                summary = "Moved once",
                startUtc = slot.AddHours(1),
                endUtc = slot.AddHours(1).AddMinutes(15),
                isAllDay = false,
            }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", "*");
        (await org.SendAsync(put)).EnsureSuccessStatusCode();

        // The per-instance edit delivered a REQUEST (with the overridden summary) to the attendee (ADR 0053).
        var inbox = await InboxDataAsync(att.Client, att.UserId);
        Assert.Contains("METHOD:REQUEST", inbox);
        Assert.Contains("Moved once", inbox);
    }

    [Fact]
    public async Task Deleting_one_occurrence_notifies_the_attendee_with_an_exdate(){
        var att = await DavTestUser.CreateDetailedAsync(factory, "exd-att");
        var org = await BearerClientAsync();
        var calendarId = await CreateCalendarAsync(org);
        var id = await CreateWeeklyWithAttendeeAsync(org, calendarId, att.Email);

        var slot = (await ExpandAsync(org, calendarId))[1].GetProperty("recurrenceId").GetDateTime();
        using var delete = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/calendars/{calendarId}/events/{id}/occurrences/{Basic(slot)}?scope=this");
        delete.Headers.TryAddWithoutValidation("If-Match", "*");
        (await org.SendAsync(delete)).EnsureSuccessStatusCode();

        var inbox = await InboxDataAsync(att.Client, att.UserId);
        Assert.Contains("METHOD:REQUEST", inbox);
        Assert.Contains("EXDATE", inbox);
    }

    private async Task<Guid> CreateWeeklyAsync(HttpClient client, Guid calendarId, int count)
    {
        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Standup",
            startUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc),
            isAllDay = false,
            recurrence = new { frequency = "WEEKLY", interval = 1, byDay = Array.Empty<string>(), count },
        });
        created.EnsureSuccessStatusCode();
        return (await Body(created)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateWeeklyWithAttendeeAsync(HttpClient client, Guid calendarId, string attendee)
    {
        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Standup",
            startUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc),
            isAllDay = false,
            organizer = AuthWebApplicationFactory.DemoAdminEmail,
            attendees = new[] { new { address = attendee } },
            recurrence = new { frequency = "WEEKLY", interval = 1, byDay = Array.Empty<string>(), count = 4 },
        });
        created.EnsureSuccessStatusCode();
        return (await Body(created)).GetProperty("id").GetGuid();
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

    private static async Task<List<JsonElement>> ExpandAsync(HttpClient client, Guid calendarId)
    {
        var expanded = await client.GetFromJsonAsync<JsonElement>(
            $"/api/calendars/{calendarId}/events?fromUtc=2026-09-01T00:00:00Z&toUtc=2026-11-01T00:00:00Z&expand=true");
        return expanded.GetProperty("items").EnumerateArray()
            .OrderBy(o => o.GetProperty("startUtc").GetDateTime()).ToList();
    }

    private static string Basic(DateTime slot) =>
        slot.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    private async Task<Guid> CreateCalendarAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" })))
            .GetProperty("id").GetGuid();

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
