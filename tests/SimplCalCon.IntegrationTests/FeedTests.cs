using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Read-only subscription feeds (ADR 0069): capability-token URL, revocable, anonymous.</summary>
public sealed class FeedTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Calendar_feed_serves_with_a_valid_token_and_revokes_on_disable()
    {
        var client = await AuthedClientAsync();
        var created = await client.PostAsJsonAsync("/api/calendars", new { name = "Feed" });
        var id = (await Body(created)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/calendars/{id}/events", new
        {
            summary = "Standup",
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        });

        // Owner enables the feed → resource carries the token.
        var enabled = await client.PutAsync($"/api/calendars/{id}/feed", null);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var token = (await Body(enabled)).GetProperty("feedToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        // The feed serves the calendar anonymously (no auth header) with a valid token.
        var anon = factory.CreateClient();
        var feed = await anon.GetAsync($"/api/calendars/{id}/feed/{token}.ics");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        Assert.Equal("text/calendar", feed.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Standup", await feed.Content.ReadAsStringAsync());

        // A wrong token is indistinguishable from "no such feed" → 404.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/calendars/{id}/feed/bogustoken.ics")).StatusCode);

        // Disabling revokes it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/calendars/{id}/feed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/calendars/{id}/feed/{token}.ics")).StatusCode);
    }

    [Fact]
    public async Task Address_book_feed_serves_vcard_with_a_valid_token()
    {
        var client = await AuthedClientAsync();
        var created = await client.PostAsJsonAsync("/api/address-books", new { name = "FeedBook" });
        var id = (await Body(created)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/address-books/{id}/contacts", new { formattedName = "Jane Feed", emails = new[] { "jane@x.test" } });

        var token = (await Body(await client.PutAsync($"/api/address-books/{id}/feed", null))).GetProperty("feedToken").GetString();

        var anon = factory.CreateClient();
        var feed = await anon.GetAsync($"/api/address-books/{id}/feed/{token}.vcf");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        Assert.Equal("text/vcard", feed.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Jane Feed", await feed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Feed_is_404_when_never_enabled()
    {
        var client = await AuthedClientAsync();
        var id = (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = "NoFeed" }))).GetProperty("id").GetGuid();

        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/calendars/{id}/feed/anything.ics")).StatusCode);
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
