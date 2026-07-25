using System.Net.Http.Headers;
using System.Net.Http.Json;
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
