using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.IntegrationTests;

public sealed class DavAuthenticationTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Dav_whoami_requires_credentials()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/dav/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Dav_whoami_succeeds_with_a_valid_app_password()
    {
        var client = factory.CreateClient();
        var (email, secret, userId) = await IssueAppPasswordForDemoAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/dav/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{secret}")));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(userId.ToString(), body);
        Assert.Contains(email, body);
    }

    [Fact]
    public async Task Dav_whoami_rejects_a_wrong_secret()
    {
        var client = factory.CreateClient();
        var (email, _, _) = await IssueAppPasswordForDemoAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/dav/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:wrong-secret")));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(string Email, string Secret, Guid UserId)> IssueAppPasswordForDemoAdminAsync()
    {
        // Ensure the host (and bootstrap seeder) has started.
        factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var normalized = AuthWebApplicationFactory.DemoAdminEmail.ToUpperInvariant();
        var admin = await dbContext.Users.FirstAsync(u => u.NormalizedEmail == normalized);

        var appPasswords = scope.ServiceProvider.GetRequiredService<IAppPasswordService>();
        var issued = await appPasswords.IssueAsync(admin.Id, "integration-test-device", default);

        return (admin.Email, issued.Secret, admin.Id);
    }
}
