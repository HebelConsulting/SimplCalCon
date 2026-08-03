using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>The role-gated admin reads (ADR 0034): the demo admin is a tenant admin.</summary>
public sealed class AdminApiTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Tenant_admin_lists_tenant_users_but_is_forbidden_the_tenants_list()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var users = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        using var doc = JsonDocument.Parse(await users.Content.ReadAsStringAsync());
        var emails = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(u => u.GetProperty("email").GetString());
        Assert.Contains(AuthWebApplicationFactory.DemoAdminEmail, emails);

        // A tenant admin is not a platform admin.
        var tenants = await client.GetAsync("/api/admin/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, tenants.StatusCode);
    }

    [Fact]
    public async Task User_list_reports_has_photo_per_user()
    {
        // Seed a profile photo for the demo admin (ADR 0035 admin-list thumbnails).
        Guid adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
            var admin = await db.Users.FirstAsync(u => u.NormalizedEmail == AuthWebApplicationFactory.DemoAdminEmail.ToUpperInvariant());
            adminId = admin.Id;
            if (!await db.UserProfilePhotos.AnyAsync(p => p.UserId == adminId))
            {
                db.UserProfilePhotos.Add(new UserProfilePhoto
                {
                    UserId = adminId, TenantId = admin.TenantId, Photo = [1, 2, 3], UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        try
        {
            var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetFromJsonAsync<JsonElement>("/api/admin/users");
            var items = response.GetProperty("items").EnumerateArray().ToList();

            Assert.All(items, u => Assert.True(u.TryGetProperty("hasPhoto", out _))); // field present on every row
            var adminItem = items.Single(u => u.GetProperty("id").GetGuid() == adminId);
            Assert.True(adminItem.GetProperty("hasPhoto").GetBoolean());              // reflects the seeded photo
        }
        finally
        {
            // The demo admin is shared across the whole run when CI uses a single
            // Postgres database (SQLite isolates per factory). Remove the seeded
            // photo so it doesn't leak into PhotoApiTests' "no photo yet" baseline.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
            await db.UserProfilePhotos.Where(p => p.UserId == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Admin_endpoints_require_authentication()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/tenants")).StatusCode);
    }
}
