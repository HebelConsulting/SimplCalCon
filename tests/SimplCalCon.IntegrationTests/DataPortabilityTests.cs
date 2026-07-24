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
    public async Task Imported_event_exposes_its_location()
    {
        var client = await AuthedClientAsync();
        var calendar = await CreateCalendarAsync(client);

        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//T//EN\r\nBEGIN:VEVENT\r\nUID:loc-1@test\r\n"
            + "SUMMARY:Offsite\r\nLOCATION:Cafe Central\r\nDTSTART:20260801T090000Z\r\nDTEND:20260801T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var result = await ImportAsync(client, $"/api/calendars/{calendar}/import", ics, "loc.ics", "skip");
        Assert.Equal(1, result.GetProperty("imported").GetInt32());

        var events = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendar}/events");
        var only = Assert.Single(events.GetProperty("items").EnumerateArray());
        Assert.Equal("Cafe Central", only.GetProperty("location").GetString());
    }

    [Fact]
    public async Task Imports_a_google_style_zip_of_ics_files()
    {
        var client = await AuthedClientAsync();
        var calendar = await CreateCalendarAsync(client);

        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Google Takeout nests the .ics files under a folder and includes unrelated files.
            await WriteEntryAsync(zip, "Takeout/Calendar/Personal.ics", Ics("zip-a@test", "Alpha"));
            await WriteEntryAsync(zip, "Takeout/Calendar/Work.ics", Ics("zip-b@test", "Beta"));
            await WriteEntryAsync(zip, "Takeout/archive_browser.html", "<html>ignore me</html>");
        }

        using var form = new MultipartFormDataContent { { new StringContent("skip"), "onConflict" } };
        var file = new ByteArrayContent(zipStream.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "calendar-export.zip");
        var response = await client.PostAsync($"/api/calendars/{calendar}/import", form);
        response.EnsureSuccessStatusCode();

        Assert.Equal(2, (await Body(response)).GetProperty("imported").GetInt32());
        var events = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendar}/events");
        Assert.Equal(2, events.GetProperty("items").EnumerateArray().Count());
    }

    private static string Ics(string uid, string summary) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//T//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n"
        + $"SUMMARY:{summary}\r\nDTSTART:20260901T090000Z\r\nDTEND:20260901T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

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
    public async Task Takeout_export_lists_owned_collections_in_the_manifest()
    {
        var client = await AuthedClientAsync();
        var name = $"Takeout {Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/api/calendars", new { name });
        var calendar = (await Body(created)).GetProperty("id").GetGuid();
        await CreateEventAsync(client, calendar, "Migrated");

        // Read only the manifest (no re-import → bounded, doesn't grow the account).
        var zip = await (await client.GetAsync("/api/takeout")).Content.ReadAsByteArrayAsync();
        var names = ZipEntryNames(zip).ToList();
        Assert.Contains("manifest.json", names);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var manifest = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        using var doc = JsonDocument.Parse(await manifest.ReadToEndAsync());
        var entry = doc.RootElement.GetProperty("Collections").EnumerateArray()
            .Single(c => c.GetProperty("Name").GetString() == name);
        Assert.Equal("calendar", entry.GetProperty("Type").GetString());
        Assert.Contains(entry.GetProperty("File").GetString(), names);
    }

    [Fact]
    public async Task Takeout_import_recreates_a_collection_from_a_manifest()
    {
        var client = await AuthedClientAsync();
        var before = await CountAsync(client, "/api/calendars");

        // A hand-built, single-collection takeout (bounded — never re-imports the whole account).
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "manifest.json", """
                {"Version":1,"ExportedAtUtc":"2026-07-23T00:00:00Z","Collections":[
                  {"Type":"calendar","Name":"Imported","ResourceName":"imported","SupportsEvents":true,"SupportsTasks":true,"File":"calendars/imported.ics"}]}
                """);
            await WriteEntryAsync(archive, "calendars/imported.ics", """
                BEGIN:VCALENDAR
                VERSION:2.0
                PRODID:-//Test//EN
                BEGIN:VEVENT
                UID:imported-event
                SUMMARY:Imported event
                DTSTART:20260715T090000Z
                DTEND:20260715T100000Z
                END:VEVENT
                END:VCALENDAR
                """);
        }

        var result = await ImportZipAsync(client, "/api/takeout", buffer.ToArray(), "skip");
        Assert.Equal(1, result.GetProperty("collectionsCreated").GetInt32());
        Assert.Equal(1, result.GetProperty("imported").GetInt32());
        Assert.Equal(before + 1, await CountAsync(client, "/api/calendars"));
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
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
