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

    private async Task<Guid> SeedForeignDeletedCalendarAsync()
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
            DeletedAt = DateTime.UtcNow,
        };
        dbContext.AddRange(owner, calendar);
        await dbContext.SaveChangesAsync();
        return calendar.Id;
    }
}
