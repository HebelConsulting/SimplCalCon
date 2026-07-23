using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Event splitting (ADR 0027): one event → two contiguous same-kind events in the same calendar.</summary>
public sealed class SplitEventTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Splitting_an_event_yields_two_contiguous_events()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, etag) = await CreateEventAsync(client, calendarId, Utc(9), Utc(17));

        var response = await SplitAsync(client, calendarId, eventId, etag, Utc(13));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        var original = body.GetProperty("original");
        var created = body.GetProperty("created");

        // Original keeps its id but ends at the split point; the copy is new and covers the tail.
        Assert.Equal(eventId, original.GetProperty("id").GetGuid());
        Assert.Equal(Utc(13), original.GetProperty("endUtc").GetDateTime().ToUniversalTime());
        Assert.NotEqual(eventId, created.GetProperty("id").GetGuid());
        Assert.Equal(Utc(13), created.GetProperty("startUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(Utc(17), created.GetProperty("endUtc").GetDateTime().ToUniversalTime());

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events");
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Split_preserves_the_full_blob_on_both_halves()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        await SeedBlobAsync(calendarId, EventBlob(
            "notes-event", extra: "DESCRIPTION:Important notes\r\nLOCATION:Room 5\r\n"));
        var eventId = await FirstEventIdAsync(client, calendarId);

        var response = await SplitAsync(client, calendarId, eventId, ifMatch: "*", Utc(13));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Both stored blobs must still carry the properties the composer would have dropped.
        var blobs = await StoredBlobsAsync(calendarId);
        Assert.Equal(2, blobs.Count);
        Assert.All(blobs, blob => Assert.Contains("Important notes", blob));
        Assert.All(blobs, blob => Assert.Contains("Room 5", blob));
    }

    [Fact]
    public async Task Splitting_a_recurring_event_is_rejected()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        await SeedBlobAsync(calendarId, EventBlob("weekly-event", extra: "RRULE:FREQ=WEEKLY\r\n"));
        var eventId = await FirstEventIdAsync(client, calendarId);

        var response = await SplitAsync(client, calendarId, eventId, ifMatch: "*", Utc(13));

        await AssertProblem(response, HttpStatusCode.BadRequest, "CANNOT_SPLIT_RECURRING");
    }

    [Fact]
    public async Task Splitting_an_all_day_event_is_rejected()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Holiday",
            startUtc = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            isAllDay = true,
        });
        var eventId = (await Body(created)).GetProperty("id").GetGuid();

        var response = await SplitAsync(client, calendarId, eventId, ifMatch: "*", Utc(12));

        await AssertProblem(response, HttpStatusCode.BadRequest, "EVENT_NOT_SPLITTABLE");
    }

    [Fact]
    public async Task Split_point_outside_the_window_is_rejected()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, etag) = await CreateEventAsync(client, calendarId, Utc(9), Utc(17));

        var response = await SplitAsync(client, calendarId, eventId, etag, Utc(18));

        await AssertProblem(response, HttpStatusCode.BadRequest, "SPLIT_POINT_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Split_without_if_match_is_precondition_required()
    {
        var client = await AuthedClientAsync();
        var calendarId = await CreateCalendarAsync(client);
        var (eventId, _) = await CreateEventAsync(client, calendarId, Utc(9), Utc(17));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/calendars/{calendarId}/events/{eventId}/split")
        {
            Content = JsonContent.Create(new { atUtc = Utc(13) }),
        };

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.SendAsync(request)).StatusCode);
    }

    private static DateTime Utc(int hour) => new(2026, 7, 15, hour, 0, 0, DateTimeKind.Utc);

    private static Task<HttpResponseMessage> SplitAsync(
        HttpClient client, Guid calendarId, Guid eventId, string ifMatch, DateTime atUtc)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/calendars/{calendarId}/events/{eventId}/split")
        {
            Content = JsonContent.Create(new { atUtc }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }

    private static async Task<Guid> CreateCalendarAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" });
        return (await Body(response)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string ETag)> CreateEventAsync(
        HttpClient client, Guid calendarId, DateTime startUtc, DateTime endUtc)
    {
        var response = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Workshop",
            startUtc,
            endUtc,
            isAllDay = false,
        });
        return ((await Body(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }

    private static async Task<Guid> FirstEventIdAsync(HttpClient client, Guid calendarId)
    {
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events");
        return list.GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private static string EventBlob(string uid, string extra) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nDTSTAMP:20260101T000000Z\r\nSUMMARY:Seeded\r\n" +
        "DTSTART:20260715T090000Z\r\nDTEND:20260715T170000Z\r\n" +
        extra +
        "END:VEVENT\r\nEND:VCALENDAR\r\n";

    private async Task SeedBlobAsync(Guid calendarId, string blob)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        await store.PutAsync(new PutObjectRequest(calendarId, $"{Guid.NewGuid():N}.ics", blob, null), CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> StoredBlobsAsync(Guid calendarId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        return await dbContext.Objects
            .Where(o => o.CollectionId == calendarId && !o.IsDeleted)
            .Select(o => o.Blob)
            .ToListAsync();
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, doc.RootElement.GetProperty("errorCode").GetString());
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
