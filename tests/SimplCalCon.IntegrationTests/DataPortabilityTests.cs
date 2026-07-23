using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Import/export per collection and account takeout round-trip (ADR 0013/0029).</summary>
public sealed class DataPortabilityTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Calendar_export_then_import_into_another_calendar_round_trips()
    {
        var client = await AuthedClientAsync();
        var source = await CreateCalendarAsync(client);
        await CreateEventAsync(client, source, "Exported");

        var ics = await (await client.GetAsync($"/api/calendars/{source}/export")).Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("Exported", ics);

        var target = await CreateCalendarAsync(client);
        var result = await ImportAsync(client, $"/api/calendars/{target}/import", ics, "events.ics", "skip");
        Assert.Equal(1, result.GetProperty("imported").GetInt32());

        var events = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{target}/events");
        var only = Assert.Single(events.GetProperty("items").EnumerateArray());
        Assert.Equal("Exported", only.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Import_conflict_mode_skips_existing_uids()
    {
        var client = await AuthedClientAsync();
        var source = await CreateCalendarAsync(client);
        await CreateEventAsync(client, source, "Dup");
        var ics = await (await client.GetAsync($"/api/calendars/{source}/export")).Content.ReadAsStringAsync();

        // Re-importing the same document into the source: the UID already exists → skipped.
        var result = await ImportAsync(client, $"/api/calendars/{source}/import", ics, "events.ics", "skip");
        Assert.Equal(0, result.GetProperty("imported").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Takeout_export_then_import_recreates_collections_and_objects()
    {
        var client = await AuthedClientAsync();
        var calendar = await CreateCalendarAsync(client);
        await CreateEventAsync(client, calendar, "Migrated");
        var book = await CreateAddressBookAsync(client);
        await client.PostAsJsonAsync($"/api/address-books/{book}/contacts", new { formattedName = "Migrated Contact" });

        var beforeCalendars = await CountAsync(client, "/api/calendars");
        var beforeBooks = await CountAsync(client, "/api/address-books");

        var zip = await (await client.GetAsync("/api/takeout")).Content.ReadAsByteArrayAsync();
        Assert.Contains("manifest.json", ZipEntryNames(zip));

        var result = await ImportZipAsync(client, "/api/takeout", zip, "skip");
        Assert.True(result.GetProperty("collectionsCreated").GetInt32() >= 2);
        Assert.True(result.GetProperty("imported").GetInt32() >= 2);

        // Ingest is always-new, so both collection lists grew.
        Assert.True(await CountAsync(client, "/api/calendars") > beforeCalendars);
        Assert.True(await CountAsync(client, "/api/address-books") > beforeBooks);
    }

    [Fact]
    public async Task Takeout_import_of_an_archive_without_a_manifest_is_rejected()
    {
        var client = await AuthedClientAsync();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("calendars/orphan.ics");
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"));
        }

        var response = await PostZipAsync(client, "/api/takeout", buffer.ToArray(), "skip");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_TAKEOUT", doc.RootElement.GetProperty("errorCode").GetString());
    }

    private static async Task<JsonElement> ImportAsync(
        HttpClient client, string url, string content, string fileName, string onConflict)
    {
        using var form = new MultipartFormDataContent { { new StringContent(onConflict), "onConflict" } };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");
        form.Add(file, "file", fileName);
        var response = await client.PostAsync(url, form);
        response.EnsureSuccessStatusCode();
        return await Body(response);
    }

    private static async Task<JsonElement> ImportZipAsync(HttpClient client, string url, byte[] zip, string onConflict)
    {
        var response = await PostZipAsync(client, url, zip, onConflict);
        response.EnsureSuccessStatusCode();
        return await Body(response);
    }

    private static Task<HttpResponseMessage> PostZipAsync(HttpClient client, string url, byte[] zip, string onConflict)
    {
        var form = new MultipartFormDataContent { { new StringContent(onConflict), "onConflict" } };
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "takeout.zip");
        return client.PostAsync(url, form);
    }

    private static IEnumerable<string> ZipEntryNames(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static async Task<int> CountAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("items").GetArrayLength();

    private static async Task<Guid> CreateCalendarAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = $"Cal {Guid.NewGuid():N}" })))
            .GetProperty("id").GetGuid();

    private static async Task<Guid> CreateAddressBookAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/address-books", new { name = $"Book {Guid.NewGuid():N}" })))
            .GetProperty("id").GetGuid();

    private static Task CreateEventAsync(HttpClient client, Guid calendarId, string summary) =>
        client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary,
            startUtc = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        });

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
