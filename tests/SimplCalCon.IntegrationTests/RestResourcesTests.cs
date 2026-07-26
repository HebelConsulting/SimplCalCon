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
    public async Task Contact_card_edit_is_lossless_over_the_store()
    {
        var client = await AuthedClientAsync();
        var bookId = (await Body(await client.PostAsJsonAsync("/api/address-books", new { name = "CardBook" }))).GetProperty("id").GetGuid();
        var contactId = (await Body(await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts",
            new { formattedName = "Temp", emails = Array.Empty<string>() }))).GetProperty("id").GetGuid();

        // Seed a rich card with data the structured form doesn't model (PHOTO + X-*).
        const string rich = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:card-uid-1\r\nFN:Old Name\r\nN:Old;Name;;;\r\n" +
            "EMAIL:old@x.test\r\nPHOTO;ENCODING=b;TYPE=JPEG:/9j/KEEPME==\r\nX-FOO:bar\r\nEND:VCARD\r\n";
        var rawEtag = (await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw")).Headers.ETag!.ToString();
        using (var seed = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/raw")
        {
            Content = new StringContent(rich, System.Text.Encoding.UTF8, "text/vcard"),
        })
        {
            seed.Headers.TryAddWithoutValidation("If-Match", rawEtag);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(seed)).StatusCode);
        }

        // Structured read.
        var cardResponse = await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/card");
        var cardEtag = cardResponse.Headers.ETag!.ToString();
        var card = JsonDocument.Parse(await cardResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Old Name", card.GetProperty("formattedName").GetString());

        // Structured edit (change the name, replace emails).
        using (var put = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/card")
        {
            Content = JsonContent.Create(new
            {
                formattedName = "New Name", givenName = "New", familyName = "Name",
                emails = new[] { new { value = "new@x.test", type = "work" } },
                phones = Array.Empty<object>(), addresses = Array.Empty<object>(),
            }),
        })
        {
            put.Headers.TryAddWithoutValidation("If-Match", cardEtag);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(put)).StatusCode);
        }

        // The card blob: modelled fields changed, unmodelled data preserved.
        var raw = await (await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw")).Content.ReadAsStringAsync();
        Assert.Contains("FN:New Name", raw);
        Assert.Contains("EMAIL;TYPE=WORK:new@x.test", raw);
        Assert.Contains("PHOTO;ENCODING=b;TYPE=JPEG:/9j/KEEPME==", raw); // preserved
        Assert.Contains("X-FOO:bar", raw);                               // preserved
        Assert.DoesNotContain("Old Name", raw);
        Assert.DoesNotContain("old@x.test", raw);
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
    public async Task Contact_photo_endpoint_serves_an_inline_card_photo()
    {
        var client = await AuthedClientAsync();
        var book = await client.PostAsJsonAsync("/api/address-books", new { name = "PhotoServe" });
        var bookId = (await Body(book)).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync($"/api/address-books/{bookId}/contacts",
            new { formattedName = "Inline Pic", emails = new[] { "inline@example.com" } });
        var contactId = (await Body(created)).GetProperty("id").GetGuid();

        // No photo yet -> 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/photo")).StatusCode);

        // Embed a known inline base64 photo (AQIDBAUGBwg= -> bytes 1..8).
        var raw = await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/raw");
        var etag = raw.Headers.ETag!.ToString();
        var withPhoto = (await raw.Content.ReadAsStringAsync())
            .Replace("END:VCARD", "PHOTO;ENCODING=b;TYPE=JPEG:AQIDBAUGBwg=\r\nEND:VCARD");
        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/address-books/{bookId}/contacts/{contactId}/raw")
        {
            Content = new StringContent(withPhoto, System.Text.Encoding.UTF8, "text/vcard"),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(put)).StatusCode);

        var photo = await client.GetAsync($"/api/address-books/{bookId}/contacts/{contactId}/photo");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await photo.Content.ReadAsByteArrayAsync());
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

    [Fact]
    public async Task Collection_colour_and_name_update_round_trips_on_both_kinds()
    {
        var client = await AuthedClientAsync();

        foreach (var kind in new[] { "calendars", "address-books" })
        {
            var created = await client.PostAsJsonAsync($"/api/{kind}", new { name = "Palette" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await Body(created)).GetProperty("id").GetGuid();
            var etag = created.Headers.ETag!.ToString();

            // Newly created: no colour yet.
            Assert.True((await Body(created)).GetProperty("color").ValueKind == JsonValueKind.Null);

            using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/{kind}/{id}")
            {
                Content = JsonContent.Create(new { name = "Palette", color = "#3B82F6" }),
            };
            put.Headers.TryAddWithoutValidation("If-Match", etag);
            var updated = await client.SendAsync(put);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("#3B82F6", (await Body(updated)).GetProperty("color").GetString());

            // The colour is persisted and returned on a fresh GET.
            var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/{kind}/{id}");
            Assert.Equal("#3B82F6", fetched.GetProperty("color").GetString());
        }
    }

    [Fact]
    public async Task Personal_colour_override_round_trips_and_clears()
    {
        var client = await AuthedClientAsync();
        var created = await client.PostAsJsonAsync("/api/calendars", new { name = "MyColour" });
        var id = (await Body(created)).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.ToString();

        // Owner sets the shared default colour.
        using (var put = new HttpRequestMessage(HttpMethod.Put, $"/api/calendars/{id}")
        {
            Content = JsonContent.Create(new { name = "MyColour", color = "#111111" }),
        })
        {
            put.Headers.TryAddWithoutValidation("If-Match", etag);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(put)).StatusCode);
        }

        // Set a personal override (no If-Match needed — it's the caller's own preference).
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/calendars/{id}/color", new { color = "#22aa33" })).StatusCode);

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{id}");
        Assert.Equal("#111111", fetched.GetProperty("color").GetString());    // owner default unchanged
        Assert.Equal("#22aa33", fetched.GetProperty("myColor").GetString());  // personal override

        // Clearing reverts to the default.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/calendars/{id}/color")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{id}");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("myColor").ValueKind);
        Assert.Equal("#111111", after.GetProperty("color").GetString());
    }

    [Fact]
    public async Task Personal_colour_rejects_a_malformed_hex()
    {
        var client = await AuthedClientAsync();
        var created = await client.PostAsJsonAsync("/api/address-books", new { name = "BadMyColour" });
        var id = (await Body(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/address-books/{id}/color", new { color = "nope" })).StatusCode);
    }

    [Fact]
    public async Task Collection_update_rejects_a_malformed_colour()
    {
        var client = await AuthedClientAsync();
        var created = await client.PostAsJsonAsync("/api/calendars", new { name = "BadColour" });
        var id = (await Body(created)).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.ToString();

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/calendars/{id}")
        {
            Content = JsonContent.Create(new { name = "BadColour", color = "not-a-colour" }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(put)).StatusCode);
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
