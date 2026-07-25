using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Bulk move/delete for events + contacts (ADR 0055).</summary>
public sealed class BulkActionsTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Bulk_delete_events_reports_success_and_removes_them()
    {
        var client = await BearerClientAsync();
        var cal = await CreateCalendarAsync(client);
        var a = await CreateEventAsync(client, cal, "A");
        var b = await CreateEventAsync(client, cal, "B");

        var response = await client.PostAsJsonAsync(
            $"/api/calendars/{cal}/events/bulk-delete", new { ids = new[] { a, b, Guid.NewGuid() } });
        var result = await Body(response);
        Assert.Equal(2, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32()); // the bogus id

        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{cal}/events");
        Assert.Empty(remaining.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Bulk_move_events_relocates_them_to_the_target_calendar()
    {
        var client = await BearerClientAsync();
        var source = await CreateCalendarAsync(client);
        var target = await CreateCalendarAsync(client);
        var a = await CreateEventAsync(client, source, "A");
        var b = await CreateEventAsync(client, source, "B");

        var response = await client.PostAsJsonAsync(
            $"/api/calendars/{source}/events/bulk-move", new { ids = new[] { a, b }, targetId = target });
        Assert.Equal(2, (await Body(response)).GetProperty("succeeded").GetInt32());

        var sourceEvents = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{source}/events");
        var targetEvents = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{target}/events");
        Assert.Empty(sourceEvents.GetProperty("items").EnumerateArray());
        Assert.Equal(2, targetEvents.GetProperty("items").EnumerateArray().Count());
    }

    [Fact]
    public async Task Bulk_delete_contacts_reports_success_and_removes_them()
    {
        var client = await BearerClientAsync();
        var book = await CreateAddressBookAsync(client);
        var a = await CreateContactAsync(client, book, "Ada");
        var b = await CreateContactAsync(client, book, "Bob");

        var response = await client.PostAsJsonAsync(
            $"/api/address-books/{book}/contacts/bulk-delete", new { ids = new[] { a, b } });
        Assert.Equal(2, (await Body(response)).GetProperty("succeeded").GetInt32());

        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{book}/contacts");
        Assert.Empty(remaining.GetProperty("items").EnumerateArray());
    }

    private async Task<Guid> CreateCalendarAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreateAddressBookAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/address-books", new { name = $"Book {Guid.NewGuid():N}" }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreateEventAsync(HttpClient client, Guid calendarId, string summary) =>
        (await Body(await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary,
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreateContactAsync(HttpClient client, Guid bookId, string name) =>
        (await Body(await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts", new { formattedName = name })))
            .GetProperty("id").GetGuid();

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<HttpClient> BearerClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
