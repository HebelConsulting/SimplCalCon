using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;

namespace SimplCalCon.Infrastructure.Identity;

internal sealed class UserAuthenticationService(
    SimplCalConDbContext dbContext,
    PasswordHashing passwordHashing,
    IClock clock,
    IOptions<LockoutOptions> lockoutOptions) : IUserAuthenticationService
{
    private readonly LockoutOptions _lockout = lockoutOptions.Value;

    public async Task<UserAuthenticationResult> AuthenticateAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user is null)
        {
            return UserAuthenticationResult.InvalidCredentials();
        }

        if (user.Status == UserStatus.Disabled)
        {
            return UserAuthenticationResult.Disabled();
        }

        if (user.LockoutEnd is { } until && until > clock.UtcNow)
        {
            return UserAuthenticationResult.LockedOut();
        }

        if (user.PasswordHash is null || !passwordHashing.Verify(user.PasswordHash, password).Succeeded)
        {
            return await RegisterFailureAsync(user, cancellationToken);
        }

        return await RegisterSuccessAsync(user, password, cancellationToken);
    }

    private async Task<UserAuthenticationResult> RegisterFailureAsync(User user, CancellationToken cancellationToken)
    {
        var lockedOut = false;
        user.AccessFailedCount++;

        if (user.AccessFailedCount >= _lockout.MaxFailedAccessAttempts)
        {
            user.LockoutEnd = clock.UtcNow + _lockout.LockoutDuration;
            user.AccessFailedCount = 0;
            lockedOut = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return lockedOut ? UserAuthenticationResult.LockedOut() : UserAuthenticationResult.InvalidCredentials();
    }

    private async Task<UserAuthenticationResult> RegisterSuccessAsync(
        User user, string password, CancellationToken cancellationToken)
    {
        var dirty = false;

        if (passwordHashing.Verify(user.PasswordHash!, password).NeedsRehash)
        {
            user.PasswordHash = passwordHashing.Hash(password);
            dirty = true;
        }

        if (user.AccessFailedCount != 0 || user.LockoutEnd is not null)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            dirty = true;
        }

        if (dirty)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return UserAuthenticationResult.Success(user);
    }
}
