using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
    public async Task Admin_endpoints_require_authentication()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/tenants")).StatusCode);
    }
}
