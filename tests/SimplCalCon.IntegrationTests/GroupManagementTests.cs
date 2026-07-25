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

/// <summary>Tenant-admin group + membership management (ADR 0059).</summary>
public sealed class GroupManagementTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Create_list_and_reject_duplicate()
    {
        var client = await BearerClientAsync();
        var name = $"Sales-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync("/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var groupId = (await Body(created)).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/admin/groups");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), g => g.GetProperty("id").GetGuid() == groupId);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/groups", new { name })).StatusCode);
    }

    [Fact]
    public async Task Add_list_and_remove_a_member()
    {
        var client = await BearerClientAsync();
        var groupId = await CreateGroupAsync(client);
        var memberId = await SeedUserAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/admin/groups/{groupId}/members/{memberId}", null)).StatusCode);

        var members = await client.GetFromJsonAsync<JsonElement>($"/api/admin/groups/{groupId}/members");
        Assert.Contains(members.GetProperty("items").EnumerateArray(), m => m.GetProperty("id").GetGuid() == memberId);

        var groups = await client.GetFromJsonAsync<JsonElement>("/api/admin/groups");
        Assert.Equal(1, groups.GetProperty("items").EnumerateArray().First(g => g.GetProperty("id").GetGuid() == groupId)
            .GetProperty("memberCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/groups/{groupId}/members/{memberId}")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/admin/groups/{groupId}/members");
        Assert.Empty(after.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Delete_removes_the_group()
    {
        var client = await BearerClientAsync();
        var groupId = await CreateGroupAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/groups/{groupId}")).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/admin/groups");
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), g => g.GetProperty("id").GetGuid() == groupId);
    }

    [Fact]
    public async Task Nesting_cycle_is_rejected()
    {
        var client = await BearerClientAsync();
        var a = await CreateGroupAsync(client);
        var b = await CreateGroupAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/admin/groups/{a}/members/{b}", null)).StatusCode);
        // Adding A into B would close the loop A→B→A.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync($"/api/admin/groups/{b}/members/{a}", null)).StatusCode);
    }

    private async Task<Guid> CreateGroupAsync(HttpClient client) =>
        (await Body(await client.PostAsJsonAsync("/api/admin/groups", new { name = $"G-{Guid.NewGuid():N}" })))
            .GetProperty("id").GetGuid();

    private async Task<Guid> SeedUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();
        var email = $"member-{Guid.NewGuid():N}@demo.test";
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = "Member",
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(), Status = UserStatus.Active, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> BearerClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
