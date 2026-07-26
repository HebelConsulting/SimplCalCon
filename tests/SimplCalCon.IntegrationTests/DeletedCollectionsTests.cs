using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>
/// Self-service recovery of a soft-deleted calendar/address book (ADR 0075): the owner can list their
/// deleted collections and restore one, and cannot restore another user's.
/// </summary>
public sealed class DeletedCollectionsTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Theory]
    [InlineData("calendars")]
    [InlineData("address-books")]
    public async Task Deleted_collection_is_listed_then_restorable(string kind)
    {
        var client = await AuthedClientAsync();

        var created = await client.PostAsJsonAsync($"/api/{kind}", new { name = "Recoverable" });
        var id = (await Body(created)).GetProperty("id").GetGuid();

        // Soft-delete it (owner-only, If-Match required).
        var etag = (await client.GetAsync($"/api/{kind}/{id}")).Headers.ETag!.ToString();
        using (var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/{kind}/{id}"))
        {
            delete.Headers.TryAddWithoutValidation("If-Match", etag);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
        }

        // Gone from the live list, present in the deleted list with a deletedAt stamp.
        Assert.DoesNotContain(await ListIdsAsync(client, $"/api/{kind}"), x => x == id);
        var deleted = await client.GetFromJsonAsync<JsonElement>($"/api/{kind}/deleted");
        var row = deleted.GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.NotEqual(default, row.GetProperty("deletedAt").GetDateTime());

        // Restore is a plain POST (no If-Match — it acts on an already-deleted collection).
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/{kind}/{id}/restore", null)).StatusCode);

        // Back in the live list, gone from the deleted list.
        Assert.Contains(await ListIdsAsync(client, $"/api/{kind}"), x => x == id);
        Assert.DoesNotContain(await ListIdsAsync(client, $"/api/{kind}/deleted"), x => x == id);
    }

    [Fact]
    public async Task Restoring_another_users_deleted_calendar_is_not_found()
    {
        var client = await AuthedClientAsync();
        var foreignId = await SeedForeignDeletedCalendarAsync();

        // Owner-only: the caller isn't the owner, so the deleted calendar is invisible/unrestorable (404, no leak).
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/calendars/{foreignId}/restore", null)).StatusCode);
        Assert.DoesNotContain(await ListIdsAsync(client, "/api/calendars/deleted"), x => x == foreignId);
    }

    [Fact]
    public async Task Purge_hard_deletes_a_deleted_calendar_and_cascades_its_events()
    {
        var client = await AuthedClientAsync();

        var id = (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = "Purgeable" }))).GetProperty("id").GetGuid();
        var eventId = (await Body(await client.PostAsJsonAsync($"/api/calendars/{id}/events", new
        {
            summary = "Doomed", startUtc = "2026-08-01T10:00:00Z", endUtc = "2026-08-01T11:00:00Z", isAllDay = false,
        }))).GetProperty("id").GetGuid();

        var etag = (await client.GetAsync($"/api/calendars/{id}")).Headers.ETag!.ToString();
        using (var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/calendars/{id}"))
        {
            delete.Headers.TryAddWithoutValidation("If-Match", etag);
            await client.SendAsync(delete);
        }

        // Purge the soft-deleted calendar — a plain DELETE on the deleted-set member, no If-Match.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/calendars/deleted/{id}")).StatusCode);

        // Gone from the deleted list, and the row + its event + revisions are physically gone.
        Assert.DoesNotContain(await ListIdsAsync(client, "/api/calendars/deleted"), x => x == id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        Assert.False(await db.Collections.AnyAsync(c => c.Id == id));
        Assert.False(await db.Objects.AnyAsync(o => o.Id == eventId));
        Assert.False(await db.ObjectRevisions.AnyAsync(r => r.ObjectId == eventId));
    }

    [Fact]
    public async Task Deleted_calendar_export_returns_its_data_for_the_owner()
    {
        var client = await AuthedClientAsync();

        var id = (await Body(await client.PostAsJsonAsync("/api/calendars", new { name = "BackupMe" }))).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/calendars/{id}/events", new
        {
            summary = "BackupMarkerEvent", startUtc = "2026-08-02T09:00:00Z", endUtc = "2026-08-02T10:00:00Z", isAllDay = false,
        });

        var etag = (await client.GetAsync($"/api/calendars/{id}")).Headers.ETag!.ToString();
        using (var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/calendars/{id}"))
        {
            delete.Headers.TryAddWithoutValidation("If-Match", etag);
            await client.SendAsync(delete);
        }

        // The pre-purge backup exports the soft-deleted collection (the normal export endpoint would 404).
        var export = await client.GetAsync($"/api/calendars/deleted/{id}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/calendar", export.Content.Headers.ContentType?.MediaType);
        var body = await export.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", body);
        Assert.Contains("BackupMarkerEvent", body);
    }

    [Fact]
    public async Task Exporting_another_users_deleted_calendar_is_not_found()
    {
        var client = await AuthedClientAsync();
        var foreignId = await SeedForeignDeletedCalendarAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/calendars/deleted/{foreignId}/export")).StatusCode);
    }

    [Fact]
    public async Task Purging_another_users_deleted_calendar_is_not_found()
    {
        var client = await AuthedClientAsync();
        var foreignId = await SeedForeignDeletedCalendarAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/calendars/deleted/{foreignId}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        Assert.True(await db.Collections.AnyAsync(c => c.Id == foreignId)); // untouched
    }

    [Fact]
    public async Task Retention_sweep_purges_only_collections_deleted_before_the_cutoff()
    {
        var oldId = await SeedForeignDeletedCalendarAsync(deletedAt: DateTime.UtcNow.AddDays(-40));
        var recentId = await SeedForeignDeletedCalendarAsync(deletedAt: DateTime.UtcNow.AddDays(-1));

        using var scope = factory.Services.CreateScope();
        var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
        var purged = await retention.PurgeDeletedCollectionsBeforeAsync(DateTime.UtcNow.AddDays(-30), 500, CancellationToken.None);

        Assert.True(purged >= 1);
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        Assert.False(await db.Collections.AnyAsync(c => c.Id == oldId));   // past cutoff → purged
        Assert.True(await db.Collections.AnyAsync(c => c.Id == recentId)); // within window → kept
    }

    private static async Task<IReadOnlyList<Guid>> ListIdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> AuthedClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SeedForeignDeletedCalendarAsync(DateTime? deletedAt = null)
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
            Name = "Foreign Deleted",
            ResourceName = $"cal-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true,
            DeletedAt = deletedAt ?? DateTime.UtcNow,
        };
        dbContext.AddRange(owner, calendar);
        await dbContext.SaveChangesAsync();
        return calendar.Id;
    }
}
