using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Identity;
using SimplCalCon.Infrastructure.Security;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class UserAuthenticationServiceTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly LockoutOptions Lockout = new()
    {
        MaxFailedAccessAttempts = 3,
        LockoutDuration = TimeSpan.FromMinutes(15),
    };

    private readonly TestDatabase _database = new();
    private readonly PasswordHashing _hashing = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private UserAuthenticationService CreateService() =>
        new(_database.CreateContext(), _hashing, _clock, Options.Create(Lockout));

    [Fact]
    public async Task Correct_password_succeeds_and_clears_failures()
    {
        var user = await SeedUserAsync(failedAttempts: 2);

        var result = await CreateService().AuthenticateAsync(user.Email, Password, default);

        Assert.Equal(UserAuthenticationStatus.Success, result.Status);
        Assert.Equal(user.Id, result.User!.Id);
        await using var verify = _database.CreateContext();
        Assert.Equal(0, (await verify.Users.FirstAsync(u => u.Id == user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task Wrong_password_locks_out_after_threshold()
    {
        var user = await SeedUserAsync();

        UserAuthenticationResult? last = null;
        for (var attempt = 0; attempt < Lockout.MaxFailedAccessAttempts; attempt++)
        {
            last = await CreateService().AuthenticateAsync(user.Email, "wrong", default);
        }

        Assert.Equal(UserAuthenticationStatus.LockedOut, last!.Status);
        await using var verify = _database.CreateContext();
        Assert.NotNull((await verify.Users.FirstAsync(u => u.Id == user.Id)).LockoutEnd);
    }

    [Fact]
    public async Task Locked_out_user_is_rejected_even_with_correct_password()
    {
        var user = await SeedUserAsync();
        user.LockoutEnd = _clock.UtcNow.AddMinutes(10);
        await SaveAsync(user);

        var result = await CreateService().AuthenticateAsync(user.Email, Password, default);

        Assert.Equal(UserAuthenticationStatus.LockedOut, result.Status);
    }

    [Fact]
    public async Task Disabled_user_is_rejected()
    {
        var user = await SeedUserAsync();
        user.Status = UserStatus.Disabled;
        await SaveAsync(user);

        var result = await CreateService().AuthenticateAsync(user.Email, Password, default);

        Assert.Equal(UserAuthenticationStatus.Disabled, result.Status);
    }

    [Fact]
    public async Task Unknown_email_returns_invalid_credentials()
    {
        var result = await CreateService().AuthenticateAsync("nobody@nowhere.test", Password, default);

        Assert.Equal(UserAuthenticationStatus.InvalidCredentials, result.Status);
    }

    private async Task<User> SeedUserAsync(int failedAttempts = 0)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            DisplayName = "Test User",
            Email = "user@example.test",
            NormalizedEmail = "USER@EXAMPLE.TEST",
            PasswordHash = _hashing.Hash(Password),
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            AccessFailedCount = failedAttempts,
            CreatedAt = _clock.UtcNow,
        };
        await SaveAsync(user);
        return user;
    }

    private async Task SaveAsync(User user)
    {
        await using var context = _database.CreateContext();
        var existing = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (existing is null)
        {
            context.Users.Add(user);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(user);
        }

        await context.SaveChangesAsync();
    }
}
