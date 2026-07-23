using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Authentication;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Identity;
using SimplCalCon.Infrastructure.Security;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class AccountActivationServiceTests
{
    private readonly TestDatabase _database = new();
    private readonly PasswordHashing _hashing = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly DefaultPasswordPolicy _policy = new(Options.Create(new PasswordPolicyOptions()));

    private AccountActivationService CreateService() =>
        new(_database.CreateContext(), _hashing, _policy, _clock);

    [Fact]
    public async Task Redeeming_activation_sets_password_and_activates_account()
    {
        var user = await SeedInvitedUserAsync();
        var issued = await CreateService().IssueAsync(user.Id, TokenPurpose.Activation, user.Id, default);

        var status = await CreateService().RedeemAsync(issued.RawToken, "a-brand-new-strong-passphrase", default);

        Assert.Equal(TokenRedemptionStatus.Success, status);
        await using var verify = _database.CreateContext();
        var updated = await verify.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Active, updated.Status);
        Assert.NotNull(updated.PasswordHash);
    }

    [Fact]
    public async Task Token_cannot_be_reused()
    {
        var user = await SeedInvitedUserAsync();
        var issued = await CreateService().IssueAsync(user.Id, TokenPurpose.Activation, user.Id, default);

        await CreateService().RedeemAsync(issued.RawToken, "a-brand-new-strong-passphrase", default);
        var second = await CreateService().RedeemAsync(issued.RawToken, "another-strong-passphrase-x", default);

        Assert.Equal(TokenRedemptionStatus.AlreadyConsumed, second);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var user = await SeedInvitedUserAsync();
        var issued = await CreateService().IssueAsync(user.Id, TokenPurpose.Activation, user.Id, default);

        _clock.Advance(TimeSpan.FromDays(8));
        var status = await CreateService().RedeemAsync(issued.RawToken, "a-brand-new-strong-passphrase", default);

        Assert.Equal(TokenRedemptionStatus.Expired, status);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var user = await SeedInvitedUserAsync();
        var issued = await CreateService().IssueAsync(user.Id, TokenPurpose.Activation, user.Id, default);

        var status = await CreateService().RedeemAsync(issued.RawToken, "short", default);

        Assert.Equal(TokenRedemptionStatus.PasswordRejected, status);
    }

    [Fact]
    public async Task Unknown_token_is_not_found()
    {
        var status = await CreateService().RedeemAsync("no-such-token", "a-brand-new-strong-passphrase", default);

        Assert.Equal(TokenRedemptionStatus.NotFound, status);
    }

    private async Task<User> SeedInvitedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            DisplayName = "Invited User",
            Email = "invited@example.test",
            NormalizedEmail = "INVITED@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Invited,
            CreatedAt = _clock.UtcNow,
        };

        await using var context = _database.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
