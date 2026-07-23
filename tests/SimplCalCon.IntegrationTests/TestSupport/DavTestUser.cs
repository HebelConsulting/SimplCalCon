using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.IntegrationTests.TestSupport;

/// <summary>
/// Creates a fresh, isolated active user (in the seeded demo tenant) with an app
/// password and a DAV-authenticated client, so each DAV test operates on an owner with
/// no pre-existing collections.
/// </summary>
internal static class DavTestUser
{
    public static async Task<(HttpClient Client, Guid UserId)> CreateAsync(
        AuthWebApplicationFactory factory, string label)
    {
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        var tenantId = await dbContext.Tenants.Select(t => t.Id).FirstAsync();

        var email = $"{label}-{Guid.NewGuid():N}@demo.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "DAV Test User",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var issued = await scope.ServiceProvider.GetRequiredService<IAppPasswordService>()
            .IssueAsync(user.Id, label, default);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{issued.Secret}")));
        return (client, user.Id);
    }
}
