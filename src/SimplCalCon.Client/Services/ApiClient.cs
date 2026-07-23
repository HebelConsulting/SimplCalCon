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

    public async Task<IReadOnlyList<ContactDto>> GetContactsAsync(Guid addressBookId) =>
        (await http.GetFromJsonAsync<Collection<ContactDto>>($"api/address-books/{addressBookId}/contacts"))?.Items ?? [];

    public Task CreateContactAsync(Guid addressBookId, string formattedName, IReadOnlyList<string> emails) =>
        http.PostAsJsonAsync($"api/address-books/{addressBookId}/contacts", new { formattedName, emails });

    public async Task<IReadOnlyList<AppPasswordDto>> GetAppPasswordsAsync() =>
        (await http.GetFromJsonAsync<Collection<AppPasswordDto>>("api/app-passwords"))?.Items ?? [];

    public async Task<CreatedAppPassword?> CreateAppPasswordAsync(string label) =>
        await (await http.PostAsJsonAsync("api/app-passwords", new { label })).Content.ReadFromJsonAsync<CreatedAppPassword>();
}
