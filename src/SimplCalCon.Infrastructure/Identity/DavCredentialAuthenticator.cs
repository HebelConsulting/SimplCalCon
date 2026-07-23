using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;

namespace SimplCalCon.Infrastructure.Identity;

/// <summary>
/// Verifies DAV Basic credentials. On a cache miss it slow-hashes the secret against
/// the user's active app passwords once, then caches the resulting identity keyed by
/// a fast hash of (email, secret) for a short TTL so subsequent polling requests skip
/// the slow hash (ADR 0005). The raw secret is never cached.
/// </summary>
internal sealed class DavCredentialAuthenticator(
    SimplCalConDbContext dbContext,
    PasswordHashing passwordHashing,
    IMemoryCache cache,
    IClock clock) : IDavCredentialAuthenticator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<DavIdentity?> AuthenticateAsync(string email, string secret, CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        var cacheKey = $"dav:{normalized}:{TokenHashing.Hash(secret)}";

        if (cache.TryGetValue(cacheKey, out DavIdentity? cached) && cached is not null)
        {
            return cached;
        }

        var user = await dbContext.Users
            .Include(u => u.AppPasswords)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            return null;
        }

        foreach (var appPassword in user.AppPasswords)
        {
            if (appPassword.RevokedAt is not null)
            {
                continue;
            }

            if (!passwordHashing.Verify(appPassword.PasswordHash, secret).Succeeded)
            {
                continue;
            }

            appPassword.LastUsedAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            var identity = new DavIdentity(user.Id, user.TenantId, user.Email, appPassword.Id);
            cache.Set(cacheKey, identity, CacheTtl);
            return identity;
        }

        return null;
    }
}
