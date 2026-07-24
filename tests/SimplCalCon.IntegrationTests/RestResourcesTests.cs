using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class RestResourcesTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Calendar_and_event_lifecycle()
    {
        var client = await AuthedClientAsync();

        var calendar = await client.PostAsJsonAsync("/api/calendars", new { name = "Work" });
        Assert.Equal(HttpStatusCode.Created, calendar.StatusCode);
        var calendarId = (await Body(calendar)).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/calendars");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == calendarId);

        var created = await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Standup",
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 15, 9, 30, 0, DateTimeKind.Utc),
            isAllDay = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var eventId = (await Body(created)).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.ToString();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/events/{eventId}");
        Assert.Equal("Standup", fetched.GetProperty("summary").GetString());
        Assert.Equal("2026-07-15T09:00:00Z", fetched.GetProperty("startUtc").GetDateTime().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/calendars/{calendarId}/events/{eventId}")
        {
            Content = JsonContent.Create(new { summary = "Renamed", startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc), isAllDay = false }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update)).StatusCode);

        // DELETE without If-Match is 428.
        Assert.Equal(HttpStatusCode.PreconditionRequired,
            (await client.DeleteAsync($"/api/calendars/{calendarId}/events/{eventId}")).StatusCode);
    }

    [Fact]
    public async Task Contact_lifecycle()
    {
        var client = await AuthedClientAsync();

        var book = await client.PostAsJsonAsync("/api/address-books", new { name = "Friends" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts", new
        {
            formattedName = "Jane Doe",
            familyName = "Doe",
            givenName = "Jane",
            emails = new[] { "jane@example.com" },
            phones = new[] { "+15551234" },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contactId = (await Body(created)).GetProperty("id").GetGuid();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/{contactId}");
        Assert.Equal("Jane Doe", fetched.GetProperty("formattedName").GetString());
        Assert.Contains("jane@example.com", fetched.GetProperty("emails").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Contact_raw_vcard_round_trip()
    {
        var client = await AuthedClientAsync();
        var book = await client.PostAsJsonAsync("/api/address-books", new { name = "Raw" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts",
            new { formattedName = "Ada Lovelace", emails = new[] { "ada@example.com" } });
        var contactId = (await Body(created)).GetProperty("id").GetGuid();

        // Read the card verbatim.
        var raw = await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw");
        Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
        Assert.Equal("text/vcard", raw.Content.Headers.ContentType?.MediaType);
        var vcard = await raw.Content.ReadAsStringAsync();
        Assert.Contains("FN:Ada Lovelace", vcard);
        var etag = raw.Headers.ETag!.ToString();

        // Edit the raw lines and save with If-Match; the structured view reflects it.
        var edited = vcard.Replace("FN:Ada Lovelace", "FN:Ada L");
        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/raw")
        {
            Content = new StringContent(edited, System.Text.Encoding.UTF8, "text/vcard"),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(put)).StatusCode);

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/{contactId}");
        Assert.Equal("Ada L", fetched.GetProperty("formattedName").GetString());
    }

    [Fact]
    public async Task Invalid_raw_vcard_is_rejected()
    {
        var client = await AuthedClientAsync();
        var book = await client.PostAsJsonAsync("/api/address-books", new { name = "Raw2" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts",
            new { formattedName = "X", emails = new[] { "x@example.com" } });
        var contactId = (await Body(created)).GetProperty("id").GetGuid();
        var etag = (await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw")).Headers.ETag!.ToString();

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/raw")
        {
            Content = new StringContent("this is not a vcard", System.Text.Encoding.UTF8, "text/vcard"),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_VCARD", problem.RootElement.GetProperty("errorCode").GetString());

        // Nothing was persisted — the original card still reads back.
        var still = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/{contactId}");
        Assert.Equal("X", still.GetProperty("formattedName").GetString());
    }

    [Fact]
    public async Task Contact_resource_reports_has_photo()
    {
        var client = await AuthedClientAsync();
        var book = await client.PostAsJsonAsync("/api/address-books", new { name = "Photos" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts",
            new { formattedName = "Pic Contact", emails = new[] { "pic@example.com" } });
        var contactId = (await Body(created)).GetProperty("id").GetGuid();

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/{contactId}");
        Assert.False(before.GetProperty("hasPhoto").GetBoolean());

        // Add a PHOTO line via the raw endpoint.
        var raw = await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw");
        var etag = raw.Headers.ETag!.ToString();
        var withPhoto = (await raw.Content.ReadAsStringAsync())
            .Replace("END:VCARD", "PHOTO;ENCODING=b;TYPE=JPEG:/9j/4AAQSkZJRg\r\nEND:VCARD");
        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/raw")
        {
            Content = new StringContent(withPhoto, System.Text.Encoding.UTF8, "text/vcard"),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(put)).StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/address-books/{bookId}/contacts/{contactId}");
        Assert.True(after.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Foreign_calendar_is_forbidden()
    {
        var client = await AuthedClientAsync();
        var foreignCalendarId = await SeedForeignCalendarAsync();

        var response = await client.GetAsync($"/api/calendars/{foreignCalendarId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INSUFFICIENT_RIGHTS", doc.RootElement.GetProperty("errorCode").GetString());
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

    private async Task<Guid> SeedForeignCalendarAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await dbContext.Tenants.Select(t => t.Id).FirstAsync();

        var email = $"foreign-{Guid.NewGuid():N}@demo.test";
        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "Foreign Owner",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = owner.Id,
            Name = "Private",
            ResourceName = $"cal-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.AddRange(owner, calendar);
        await dbContext.SaveChangesAsync();
        return calendar.Id;
    }
}
