using System.Net.Http.Headers;
using System.Net.Http.Json;
using SimplCalCon.Client.Models;

namespace SimplCalCon.Client.Services;

/// <summary>Typed access to the /api REST surface (ADR 0009). Tokens are attached by the HttpClient handler.</summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<CalendarDto>> GetCalendarsAsync() =>
        (await http.GetFromJsonAsync<Collection<CalendarDto>>("api/calendars"))?.Items ?? [];

    public async Task<CalendarDto?> CreateCalendarAsync(string name) =>
        await (await http.PostAsJsonAsync("api/calendars", new { name })).Content.ReadFromJsonAsync<CalendarDto>();

    public async Task<IReadOnlyList<AddressBookDto>> GetAddressBooksAsync() =>
        (await http.GetFromJsonAsync<Collection<AddressBookDto>>("api/address-books"))?.Items ?? [];

    public async Task<AddressBookDto?> CreateAddressBookAsync(string name) =>
        await (await http.PostAsJsonAsync("api/address-books", new { name })).Content.ReadFromJsonAsync<AddressBookDto>();

    public async Task<IReadOnlyList<EventDto>> GetEventsAsync(Guid calendarId) =>
        (await http.GetFromJsonAsync<Collection<EventDto>>($"api/calendars/{calendarId}/events"))?.Items ?? [];

    public Task CreateEventAsync(Guid calendarId, string summary, DateTime startUtc, DateTime? endUtc, bool isAllDay) =>
        http.PostAsJsonAsync($"api/calendars/{calendarId}/events", new { summary, startUtc, endUtc, isAllDay });

    // Split an event at a point in time into two (ADR 0027). If-Match:* — the UI splits the current version.
    public async Task SplitEventAsync(Guid calendarId, Guid eventId, DateTime atUtc)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/calendars/{calendarId}/events/{eventId}/split")
        {
            Content = JsonContent.Create(new { atUtc }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ContactDto>> GetContactsAsync(Guid addressBookId) =>
        (await http.GetFromJsonAsync<Collection<ContactDto>>($"api/address-books/{addressBookId}/contacts"))?.Items ?? [];

    public Task CreateContactAsync(Guid addressBookId, string formattedName, IReadOnlyList<string> emails) =>
        http.PostAsJsonAsync($"api/address-books/{addressBookId}/contacts", new { formattedName, emails });

    public async Task<IReadOnlyList<AppPasswordDto>> GetAppPasswordsAsync() =>
        (await http.GetFromJsonAsync<Collection<AppPasswordDto>>("api/app-passwords"))?.Items ?? [];

    public async Task<CreatedAppPassword?> CreateAppPasswordAsync(string label) =>
        await (await http.PostAsJsonAsync("api/app-passwords", new { label })).Content.ReadFromJsonAsync<CreatedAppPassword>();

    // Sharing (ADR 0007, 0023). `kind` is "calendars" or "address-books".
    public async Task<IReadOnlyList<ShareDto>> GetSharesAsync(string kind, Guid collectionId) =>
        (await http.GetFromJsonAsync<Collection<ShareDto>>($"api/{kind}/{collectionId}/shares"))?.Items ?? [];

    public Task PutShareAsync(string kind, Guid collectionId, Guid principalId, IReadOnlyList<string> rights) =>
        http.PutAsJsonAsync($"api/{kind}/{collectionId}/shares/{principalId}", new { rights });

    public Task DeleteShareAsync(string kind, Guid collectionId, Guid principalId) =>
        http.DeleteAsync($"api/{kind}/{collectionId}/shares/{principalId}");

    public async Task<IReadOnlyList<PrincipalDto>> SearchPrincipalsAsync(string query) =>
        (await http.GetFromJsonAsync<Collection<PrincipalDto>>($"api/principals?q={Uri.EscapeDataString(query)}"))?.Items ?? [];

    // Trash & version history (ADR 0028). `kind` is "calendars" or "address-books"; its child resource is events/contacts.
    private static string Child(string kind) => kind == "calendars" ? "events" : "contacts";

    public async Task<IReadOnlyList<TrashItemDto>> GetTrashAsync(string kind, Guid collectionId) =>
        (await http.GetFromJsonAsync<Collection<TrashItemDto>>($"api/{kind}/{collectionId}/{Child(kind)}/trash"))?.Items ?? [];

    public Task RestoreAsync(string kind, Guid collectionId, Guid id) =>
        http.PostAsync($"api/{kind}/{collectionId}/{Child(kind)}/trash/{id}/restore", null);

    public Task PurgeAsync(string kind, Guid collectionId, Guid id) =>
        http.DeleteAsync($"api/{kind}/{collectionId}/{Child(kind)}/trash/{id}");

    public Task EmptyTrashAsync(string kind, Guid collectionId) =>
        http.DeleteAsync($"api/{kind}/{collectionId}/{Child(kind)}/trash");

    public async Task<IReadOnlyList<RevisionDto>> GetRevisionsAsync(string kind, Guid collectionId, Guid id) =>
        (await http.GetFromJsonAsync<Collection<RevisionDto>>($"api/{kind}/{collectionId}/{Child(kind)}/{id}/revisions"))?.Items ?? [];

    public Task RestoreRevisionAsync(string kind, Guid collectionId, Guid id, long number) =>
        http.PostAsync($"api/{kind}/{collectionId}/{Child(kind)}/{id}/revisions/{number}/restore", null);

    // Data portability (ADR 0013/0029). `kind` is "calendars" or "address-books".
    public async Task<ImportResultDto?> ImportCollectionAsync(
        string kind, Guid collectionId, byte[] content, string fileName, string onConflict)
    {
        using var form = BuildForm(content, fileName, kind == "calendars" ? "text/calendar" : "text/vcard", onConflict);
        var response = await http.PostAsync($"api/{kind}/{collectionId}/import", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportResultDto>();
    }

    public Task<byte[]> ExportCollectionAsync(string kind, Guid collectionId) =>
        http.GetByteArrayAsync($"api/{kind}/{collectionId}/export");

    public Task<byte[]> DownloadTakeoutAsync() => http.GetByteArrayAsync("api/takeout");

    public async Task<TakeoutImportResultDto?> ImportTakeoutAsync(byte[] zip, string fileName, string onConflict)
    {
        using var form = BuildForm(zip, fileName, "application/zip", onConflict);
        var response = await http.PostAsync("api/takeout", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TakeoutImportResultDto>();
    }

    private static MultipartFormDataContent BuildForm(byte[] content, string fileName, string contentType, string onConflict)
    {
        var form = new MultipartFormDataContent { { new StringContent(onConflict), "onConflict" } };
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return form;
    }
}
