using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Identity;
using SimplCalCon.Infrastructure.Security;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class AppPasswordAuthenticationTests
{
    private readonly TestDatabase _database = new();
    private readonly PasswordHashing _hashing = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private AppPasswordService AppPasswords() => new(_database.CreateContext(), _hashing, _clock);

    private DavCredentialAuthenticator Authenticator() =>
        new(_database.CreateContext(), _hashing, _cache, _clock);

    [Fact]
    public async Task Issued_app_password_authenticates_the_owner()
    {
        var user = await SeedActiveUserAsync();
        var issued = await AppPasswords().IssueAsync(user.Id, "iPhone", default);

        var identity = await Authenticator().AuthenticateAsync(user.Email, issued.Secret, default);

        Assert.NotNull(identity);
        Assert.Equal(user.Id, identity!.UserId);
        Assert.Equal(issued.AppPassword.Id, identity.AppPasswordId);
    }

    [Fact]
    public async Task Wrong_secret_is_rejected()
    {
        var user = await SeedActiveUserAsync();
        await AppPasswords().IssueAsync(user.Id, "iPhone", default);

        var identity = await Authenticator().AuthenticateAsync(user.Email, "not-the-secret", default);

        Assert.Null(identity);
    }

    [Fact]
    public async Task Revoked_app_password_is_rejected()
    {
        var user = await SeedActiveUserAsync();
        var issued = await AppPasswords().IssueAsync(user.Id, "iPhone", default);

        await using (var context = _database.CreateContext())
        {
            var appPassword = await context.AppPasswords.FirstAsync(a => a.Id == issued.AppPassword.Id);
            appPassword.RevokedAt = _clock.UtcNow;
            await context.SaveChangesAsync();
        }

        var identity = await Authenticator().AuthenticateAsync(user.Email, issued.Secret, default);

        Assert.Null(identity);
    }

    private async Task<User> SeedActiveUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            DisplayName = "Device Owner",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = _hashing.Hash("irrelevant-account-password"),
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = _clock.UtcNow,
        };

        await using var context = _database.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
