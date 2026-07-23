using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Trash (soft-delete → restore/purge) and version history (ADR 0011/0028) for objects.</summary>
public sealed class TrashHistoryTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Event_can_be_trashed_listed_and_restored()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, etag) = await CreateEventAsync(client, calendarId, "Standup");

        await TrashAsync(client, calendarId, eventId, etag);

        // Gone from the live list, present in trash with a deletedAt.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events"))
            .GetProperty("items").EnumerateArray());
        var trash = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/trash");
        var trashed = Assert.Single(trash.GetProperty("items").EnumerateArray());
        Assert.Equal(eventId, trashed.GetProperty("id").GetGuid());
        Assert.NotNull(trashed.GetProperty("deletedAt").GetString());

        // Restore brings it back to the live list.
        var restore = await client.PostAsync($"/api/calendars/{calendarId}/events/trash/{eventId}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events"))
            .GetProperty("items").EnumerateArray());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/trash"))
            .GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Purging_a_trashed_event_removes_it_permanently()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, etag) = await CreateEventAsync(client, calendarId, "Temp");
        await TrashAsync(client, calendarId, eventId, etag);

        var purge = await client.DeleteAsync($"/api/calendars/{calendarId}/events/trash/{eventId}");
        Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);

        // Trash is empty and history is gone (the object no longer exists).
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/trash"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/calendars/{calendarId}/events/{eventId}/revisions")).StatusCode);
    }

    [Fact]
    public async Task Empty_trash_purges_every_trashed_item()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        foreach (var name in new[] { "A", "B", "C" })
        {
            var (id, etag) = await CreateEventAsync(client, calendarId, name);
            await TrashAsync(client, calendarId, id, etag);
        }

        var empty = await client.DeleteAsync($"/api/calendars/{calendarId}/events/trash");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/trash"))
            .GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Version_history_records_changes_and_a_prior_revision_can_be_restored()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, etag) = await CreateEventAsync(client, calendarId, "Original");

        // Rename → a second revision.
        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/calendars/{calendarId}/events/{eventId}")
        {
            Content = JsonContent.Create(new
            {
                summary = "Renamed",
                startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
                isAllDay = false,
            }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update)).StatusCode);

        var revisions = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{eventId}/revisions");
        var items = revisions.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Updated", items[0].GetProperty("operation").GetString());   // newest first
        Assert.Equal(2, items[0].GetProperty("revisionNumber").GetInt64());
        Assert.Equal("Created", items[1].GetProperty("operation").GetString());

        // Restore revision 1 → summary reverts and a Restored revision is appended.
        var restore = await client.PostAsync($"/api/calendars/{calendarId}/events/{eventId}/revisions/1/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal("Original", (await Body(restore)).GetProperty("summary").GetString());

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{eventId}/revisions");
        var latest = after.GetProperty("items").EnumerateArray().First();
        Assert.Equal("Restored", latest.GetProperty("operation").GetString());
        Assert.Equal(3, latest.GetProperty("revisionNumber").GetInt64());
    }

    [Fact]
    public async Task Restoring_a_missing_revision_is_not_found()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, _) = await CreateEventAsync(client, calendarId, "Once");

        var response = await client.PostAsync($"/api/calendars/{calendarId}/events/{eventId}/revisions/99/restore", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("REVISION_NOT_FOUND", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Contact_can_be_trashed_and_restored()
    {
        var client = await AuthedClientAsync();
        var book = await client.PostAsJsonAsync("/api/address-books", new { name = $"Book {Guid.NewGuid():N}" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts", new { formattedName = "Jane Doe" });
        var contactId = (await Body(created)).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.ToString();

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/address-books/{bookId}/contacts/{contactId}");
        delete.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);

        var trashed = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/trash"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(contactId, trashed.GetProperty("id").GetGuid());

        var restore = await client.PostAsync($"/api/address-books/{bookId}/contacts/trash/{contactId}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts"))
            .GetProperty("items").EnumerateArray());
    }

    private static async Task<Guid> CreateCalendarAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" });
        return (await Body(response)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string ETag)> CreateEventAsync(HttpClient client, Guid calendarId, string summary)
    {
        var response = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary,
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        });
        return ((await Body(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }

    private static async Task TrashAsync(HttpClient client, Guid calendarId, Guid eventId, string etag)
    {
        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/calendars/{calendarId}/events/{eventId}");
        delete.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> AuthedClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
