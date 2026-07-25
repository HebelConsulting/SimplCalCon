using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>The owner's "shared by me" aggregate (ADR 0058).</summary>
public sealed class SharedByMeTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Lists_owned_collections_that_have_grants_with_their_shares()
    {
        var client = await BearerClientAsync();
        var sharedId = await CreateCalendarAsync(client, "Team");
        var privateId = await CreateCalendarAsync(client, "Private");
        var granteeId = await SeedUserAsync();

        var put = await client.PutAsJsonAsync(
            $"/api/calendars/{sharedId}/shares/{granteeId}", new { rights = new[] { "read" } });
        put.EnsureSuccessStatusCode();

        var byMe = await client.GetFromJsonAsync<JsonElement>("/api/shared-by-me");
        var items = byMe.GetProperty("items").EnumerateArray().ToList();

        var shared = items.Single(i => i.GetProperty("id").GetGuid() == sharedId);
        Assert.Equal("calendars", shared.GetProperty("kind").GetString());
        var share = shared.GetProperty("shares").EnumerateArray().Single();
        Assert.Equal(granteeId, share.GetProperty("principalId").GetGuid());
        Assert.Contains("read", share.GetProperty("rights").EnumerateArray().Select(r => r.GetString()));

        // A calendar with no grants is not listed.
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == privateId);
    }

    private async Task<Guid> CreateCalendarAsync(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/calendars", new { name })).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private async Task<Guid> SeedUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();
        var email = $"grantee-{Guid.NewGuid():N}@demo.test";
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = "Grantee",
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(), Status = UserStatus.Active, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
    }

    private async Task<HttpClient> BearerClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
