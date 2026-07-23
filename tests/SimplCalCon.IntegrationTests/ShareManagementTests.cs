using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class ShareManagementTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Owner_grants_lists_and_revokes_a_share()
    {
        var client = await AuthedClientAsync();
        var calendarId = (await (await client.PostAsJsonAsync("/api/calendars", new { name = "Team" })).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var granteeId = await SeedUserAsync();

        var put = await client.PutAsJsonAsync($"/api/calendars/{calendarId}/shares/{granteeId}", new { rights = new[] { "read", "write-content" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var shares = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/shares");
        var grant = shares.GetProperty("items").EnumerateArray().Single(g => g.GetProperty("principalId").GetGuid() == granteeId);
        Assert.Contains("write-content", grant.GetProperty("rights").EnumerateArray().Select(r => r.GetString()));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/calendars/{calendarId}/shares/{granteeId}")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/calendars/{calendarId}/shares");
        Assert.Empty(after.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_grant_enables_rest_access()
    {
        var client = await AuthedClientAsync();
        var (calendarId, _) = await SeedForeignCalendarAsync();

        // Before the grant: forbidden. After granting the demo admin read: allowed.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/calendars/{calendarId}")).StatusCode);
        await GrantAsync(calendarId, await DemoAdminIdAsync(), AclRight.Read);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/calendars/{calendarId}")).StatusCode);
    }

    [Fact]
    public async Task Managing_shares_without_the_right_is_forbidden()
    {
        var client = await AuthedClientAsync();
        var (calendarId, _) = await SeedForeignCalendarAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/calendars/{calendarId}/shares")).StatusCode);
    }

    [Fact]
    public async Task Principals_search_finds_tenant_users()
    {
        var client = await AuthedClientAsync();

        var results = await client.GetFromJsonAsync<JsonElement>("/api/principals?q=admin");

        Assert.Contains(results.GetProperty("items").EnumerateArray(),
            p => p.GetProperty("email").GetString() == AuthWebApplicationFactory.DemoAdminEmail);
    }

    [Fact]
    public async Task Dav_privilege_set_reflects_a_read_only_share()
    {
        var (ownerClient, ownerId) = await DavTestUser.CreateAsync(factory, "share-owner");
        var (shareeClient, shareeId) = await DavTestUser.CreateAsync(factory, "share-sharee");

        var book = $"b{Guid.NewGuid():N}";
        var collection = $"/dav/addressbooks/{ownerId}/{book}";
        await ownerClient.SendAsync(new HttpRequestMessage(new HttpMethod("MKCOL"), collection));

        var bookId = await AddressBookIdAsync(ownerId, book);
        await GrantAsync(bookId, shareeId, AclRight.Read);

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), $"{collection}/");
        request.Headers.Add("Depth", "0");
        request.Content = new StringContent(
            "<propfind xmlns=\"DAV:\"><prop><current-user-privilege-set/></prop></propfind>", Encoding.UTF8, "application/xml");
        var response = await shareeClient.SendAsync(request);

        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace dav = "DAV:";
        var privileges = doc.Descendants(dav + "privilege").Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("read", privileges);
        Assert.DoesNotContain("write", privileges);
    }

    private async Task GrantAsync(Guid collectionId, Guid principalId, AclRight rights)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAclService>().GrantAsync(collectionId, principalId, rights, default);
    }

    private async Task<Guid> DemoAdminIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var normalized = AuthWebApplicationFactory.DemoAdminEmail.ToUpperInvariant();
        return await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => u.Id).FirstAsync();
    }

    private async Task<Guid> AddressBookIdAsync(Guid ownerId, string resourceName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        return await db.AddressBooks.Where(a => a.OwnerId == ownerId && a.ResourceName == resourceName).Select(a => a.Id).FirstAsync();
    }

    private async Task<Guid> SeedUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();
        var email = $"grantee-{Guid.NewGuid():N}@demo.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "Grantee",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(Guid CalendarId, Guid OwnerId)> SeedForeignCalendarAsync()
    {
        var ownerId = await SeedUserAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await db.Users.Where(u => u.Id == ownerId).Select(u => u.TenantId!.Value).FirstAsync();
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = "Private",
            ResourceName = $"cal-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
        };
        db.Calendars.Add(calendar);
        await db.SaveChangesAsync();
        return (calendar.Id, ownerId);
    }

    private async Task<HttpClient> AuthedClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
