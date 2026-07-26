using System.Net.Http.Headers;
using System.Net.Http.Json;
using SimplCalCon.Client.Models;

namespace SimplCalCon.Client.Services;

/// <summary>Typed access to the /api REST surface (ADR 0009). Tokens are attached by the HttpClient handler.</summary>
public sealed class ApiClient(HttpClient http)
{
    public Task<MeDto?> GetMeAsync() => http.GetFromJsonAsync<MeDto>("api/me");

    /// <summary>Fetches a profile photo via the authenticated client and returns it as a data URL (null if none).</summary>
    public async Task<string?> GetPhotoDataUrlAsync(string path)
    {
        using var response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    public async Task PutPhotoAsync(string path, byte[] png)
    {
        using var content = new ByteArrayContent(png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await http.PutAsync(path, content)).EnsureSuccessStatusCode();
    }

    public Task DeletePhotoAsync(string path) => http.DeleteAsync(path);

    public async Task<IReadOnlyList<TenantDto>> GetTenantsAsync() =>
        (await http.GetFromJsonAsync<Collection<TenantDto>>("api/admin/tenants"))?.Items ?? [];

    public async Task<IReadOnlyList<AdminUserDto>> GetTenantUsersAsync() =>
        (await http.GetFromJsonAsync<Collection<AdminUserDto>>("api/admin/users"))?.Items ?? [];

    // Tenant SMTP / iMIP settings (ADR 0047).
    public Task<TenantEmailSettingsDto?> GetTenantEmailSettingsAsync() =>
        http.GetFromJsonAsync<TenantEmailSettingsDto>("api/admin/email-settings");

    public Task SaveTenantEmailSettingsAsync(object body) =>
        http.PutAsJsonAsync("api/admin/email-settings", body);

    /// <summary>Sends a test email via the tenant's saved SMTP settings; null on success, else the failure detail.</summary>
    public async Task<string?> SendTestEmailAsync(string to)
    {
        var response = await http.PostAsJsonAsync("api/admin/email-settings/test", new { to });
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
        return string.IsNullOrWhiteSpace(body) ? $"Failed ({(int)response.StatusCode})." : body;
    }

    public async Task<IReadOnlyList<CalendarDto>> GetCalendarsAsync() =>
        (await http.GetFromJsonAsync<Collection<CalendarDto>>("api/calendars"))?.Items ?? [];

    // Deleted-collection recovery (ADR 0075): the owner's soft-deleted collections + one-click restore.
    public async Task<IReadOnlyList<CalendarDto>> GetDeletedCalendarsAsync() =>
        (await http.GetFromJsonAsync<Collection<CalendarDto>>("api/calendars/deleted"))?.Items ?? [];

    public async Task<IReadOnlyList<AddressBookDto>> GetDeletedAddressBooksAsync() =>
        (await http.GetFromJsonAsync<Collection<AddressBookDto>>("api/address-books/deleted"))?.Items ?? [];

    public Task RestoreCollectionAsync(string kind, Guid id) =>
        http.PostAsync($"api/{kind}/{id}/restore", null);

    // Permanently purge a soft-deleted collection (ADR 0077) — irreversible.
    public Task PurgeCollectionAsync(string kind, Guid id) =>
        http.DeleteAsync($"api/{kind}/deleted/{id}");

    // Mandatory pre-purge backup (ADR 0078): export a soft-deleted collection's data.
    public Task<byte[]> ExportDeletedCollectionAsync(string kind, Guid id) =>
        http.GetByteArrayAsync($"api/{kind}/deleted/{id}/export");

    public async Task<CalendarDto?> CreateCalendarAsync(string name) =>
        await (await http.PostAsJsonAsync("api/calendars", new { name })).Content.ReadFromJsonAsync<CalendarDto>();

    public async Task<IReadOnlyList<AddressBookDto>> GetAddressBooksAsync() =>
        (await http.GetFromJsonAsync<Collection<AddressBookDto>>("api/address-books"))?.Items ?? [];

    public async Task<AddressBookDto?> CreateAddressBookAsync(string name) =>
        await (await http.PostAsJsonAsync("api/address-books", new { name })).Content.ReadFromJsonAsync<AddressBookDto>();

    // Invitations — the web view of the schedule-inbox (ADR 0045).
    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync() =>
        (await http.GetFromJsonAsync<Collection<InvitationDto>>("api/invitations"))?.Items ?? [];

    public Task RespondToInvitationAsync(string resourceName, string response) =>
        http.PostAsJsonAsync("api/invitations/respond", new { resourceName, response });

    public async Task<int> GetInvitationCountAsync()
    {
        try
        {
            return (await http.GetFromJsonAsync<InvitationCountDto>("api/invitations/count"))?.Count ?? 0;
        }
        catch
        {
            return 0; // not signed in yet / transient — no badge
        }
    }

    public async Task<IReadOnlyList<EventDto>> GetEventsAsync(Guid calendarId) =>
        (await http.GetFromJsonAsync<Collection<EventDto>>($"api/calendars/{calendarId}/events"))?.Items ?? [];

    public Task<EventDto?> GetEventAsync(Guid calendarId, Guid eventId) =>
        http.GetFromJsonAsync<EventDto>($"api/calendars/{calendarId}/events/{eventId}");

    // Expanded occurrences across a window for the grid (ADR 0050): recurring events appear on every date.
    public async Task<IReadOnlyList<EventDto>> GetEventOccurrencesAsync(Guid calendarId, DateTime fromUtc, DateTime toUtc) =>
        (await http.GetFromJsonAsync<Collection<EventDto>>(
            $"api/calendars/{calendarId}/events?fromUtc={fromUtc:o}&toUtc={toUtc:o}&expand=true"))?.Items ?? [];

    public Task CreateEventAsync(Guid calendarId, object body) =>
        http.PostAsJsonAsync($"api/calendars/{calendarId}/events", body);

    // Update an event. If-Match:* — the UI edits the current version (consistent with split/restore).
    public async Task UpdateEventAsync(Guid calendarId, Guid eventId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/calendars/{calendarId}/events/{eventId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    public async Task DeleteEventAsync(Guid calendarId, Guid eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/calendars/{calendarId}/events/{eventId}");
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    // Per-instance recurring edits (ADR 0051). scope = "this" | "following".
    public async Task UpdateOccurrenceAsync(Guid calendarId, Guid eventId, DateTime recurrenceIdUtc, string scope, object body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"api/calendars/{calendarId}/events/{eventId}/occurrences/{RecurrenceIdPath(recurrenceIdUtc)}?scope={scope}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    public async Task DeleteOccurrenceAsync(Guid calendarId, Guid eventId, DateTime recurrenceIdUtc, string scope)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"api/calendars/{calendarId}/events/{eventId}/occurrences/{RecurrenceIdPath(recurrenceIdUtc)}?scope={scope}");
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static string RecurrenceIdPath(DateTime recurrenceIdUtc) =>
        recurrenceIdUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    public async Task<FreeBusyDto?> GetFreeBusyAsync(string address, DateTime fromUtc, DateTime toUtc) =>
        await http.GetFromJsonAsync<FreeBusyDto>(
            $"api/free-busy?address={Uri.EscapeDataString(address)}&fromUtc={fromUtc:o}&toUtc={toUtc:o}");

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

    public async Task<ContactDto?> CreateContactAsync(Guid addressBookId, string formattedName, IReadOnlyList<string> emails) =>
        await (await http.PostAsJsonAsync($"api/address-books/{addressBookId}/contacts", new { formattedName, emails }))
            .Content.ReadFromJsonAsync<ContactDto>();

    /// <summary>The contact's raw vCard text plus its ETag (for a conditional save).</summary>
    public async Task<(string Vcard, string? ETag)> GetContactRawAsync(Guid addressBookId, Guid id)
    {
        using var response = await http.GetAsync($"api/address-books/{addressBookId}/contacts/{id}/raw");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(), response.Headers.ETag?.ToString());
    }

    /// <summary>
    /// Fetches the contact's photo through the server (ADR 0037) — the server resolves inline or
    /// external PHOTOs and caches them — as a data URL, or null if the contact has no photo.
    /// </summary>
    public async Task<string?> GetContactPhotoDataUrlAsync(Guid addressBookId, Guid id)
    {
        using var response = await http.GetAsync($"api/address-books/{addressBookId}/contacts/{id}/photo");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>Saves the raw vCard. Returns null on success, or the server's rejection message (invalid card, conflict, …).</summary>
    public async Task<string?> PutContactRawAsync(Guid addressBookId, Guid id, string vcard, string? etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/address-books/{addressBookId}/contacts/{id}/raw")
        {
            Content = new StringContent(vcard, System.Text.Encoding.UTF8, "text/vcard"),
        };
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
            return problem?.Detail ?? $"Save failed ({(int)response.StatusCode}).";
        }
        catch
        {
            return $"Save failed ({(int)response.StatusCode}).";
        }
    }

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

    public async Task<IReadOnlyList<SharedCollectionDto>> GetSharedWithMeAsync() =>
        (await http.GetFromJsonAsync<Collection<SharedCollectionDto>>("api/shared-with-me"))?.Items ?? [];

    public async Task<IReadOnlyList<SharedByMeDto>> GetSharedByMeAsync() =>
        (await http.GetFromJsonAsync<Collection<SharedByMeDto>>("api/shared-by-me"))?.Items ?? [];

    public async Task<IReadOnlyList<PrincipalDto>> SearchPrincipalsAsync(string query) =>
        (await http.GetFromJsonAsync<Collection<PrincipalDto>>($"api/principals?q={Uri.EscapeDataString(query)}"))?.Items ?? [];

    // Group management (ADR 0059, tenant-admin).
    public async Task<IReadOnlyList<GroupDto>> GetGroupsAsync() =>
        (await http.GetFromJsonAsync<Collection<GroupDto>>("api/admin/groups"))?.Items ?? [];

    public async Task<HttpResponseMessage> CreateGroupAsync(string name) =>
        await http.PostAsJsonAsync("api/admin/groups", new { name });

    public Task DeleteGroupAsync(Guid groupId) => http.DeleteAsync($"api/admin/groups/{groupId}");

    public async Task<IReadOnlyList<GroupMemberDto>> GetGroupMembersAsync(Guid groupId) =>
        (await http.GetFromJsonAsync<Collection<GroupMemberDto>>($"api/admin/groups/{groupId}/members"))?.Items ?? [];

    public async Task<HttpResponseMessage> AddGroupMemberAsync(Guid groupId, Guid principalId) =>
        await http.PutAsync($"api/admin/groups/{groupId}/members/{principalId}", null);

    public Task RemoveGroupMemberAsync(Guid groupId, Guid principalId) =>
        http.DeleteAsync($"api/admin/groups/{groupId}/members/{principalId}");

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
        string kind, Guid collectionId, byte[] content, string fileName, string onConflict,
        bool separateCollections = false, bool mergeByName = true)
    {
        var contentType = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : (kind == "calendars" ? "text/calendar" : "text/vcard");
        using var form = BuildForm(content, fileName, contentType, onConflict);
        if (separateCollections)
        {
            form.Add(new StringContent("true"), "separateCollections");
            form.Add(new StringContent(mergeByName ? "true" : "false"), "mergeByName");
        }

        var response = await http.PostAsync($"api/{kind}/{collectionId}/import", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportResultDto>();
    }

    // Collection management (ADR 0041) + entry move (ADR 0042). `kind` is "calendars" or "address-books".
    // If-Match:* — the UI operates on the current version.
    public async Task<string?> UpdateCollectionAsync(string kind, Guid id, string name, string? color) =>
        await SendWithIfMatchAsync(HttpMethod.Put, $"api/{kind}/{id}", new { name, color });

    // The caller's personal colour override (ADR 0066): set or clear (revert to the collection default).
    public Task SetMyColorAsync(string kind, Guid id, string color) =>
        http.PutAsJsonAsync($"api/{kind}/{id}/color", new { color });

    public Task ClearMyColorAsync(string kind, Guid id) =>
        http.DeleteAsync($"api/{kind}/{id}/color");

    // Subscription feed (ADR 0069): enable/reset (fresh token) or disable, owner-only.
    public async Task EnableFeedAsync(string kind, Guid id) =>
        (await http.PutAsync($"api/{kind}/{id}/feed", null)).EnsureSuccessStatusCode();

    public Task DisableFeedAsync(string kind, Guid id) => http.DeleteAsync($"api/{kind}/{id}/feed");

    public Task DeleteCollectionAsync(string kind, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/{kind}/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        return http.SendAsync(request);
    }

    public async Task<string?> MoveEntryAsync(string kind, Guid collectionId, Guid entryId, Guid targetId) =>
        await SendWithIfMatchAsync(
            HttpMethod.Post, $"api/{kind}/{collectionId}/{Child(kind)}/{entryId}/move", new { targetId });

    // Bulk move/delete (ADR 0055). If-Match-exempt; returns an aggregate result.
    public async Task<BulkResultDto?> BulkDeleteAsync(string kind, Guid collectionId, IReadOnlyList<Guid> ids) =>
        await (await http.PostAsJsonAsync($"api/{kind}/{collectionId}/{Child(kind)}/bulk-delete", new { ids }))
            .Content.ReadFromJsonAsync<BulkResultDto>();

    public async Task<BulkResultDto?> BulkMoveAsync(string kind, Guid collectionId, IReadOnlyList<Guid> ids, Guid targetId) =>
        await (await http.PostAsJsonAsync($"api/{kind}/{collectionId}/{Child(kind)}/bulk-move", new { ids, targetId }))
            .Content.ReadFromJsonAsync<BulkResultDto>();

    /// <summary>Sends a body with If-Match:*; returns null on success or the server's problem detail.</summary>
    private async Task<string?> SendWithIfMatchAsync(HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return (await response.Content.ReadFromJsonAsync<ProblemDto>())?.Detail ?? $"Failed ({(int)response.StatusCode}).";
        }
        catch
        {
            return $"Failed ({(int)response.StatusCode}).";
        }
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
