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

            // Record last-used with a direct UPDATE, bypassing the optimistic-concurrency
            // token. Native clients (Thunderbird especially) fire many /dav requests in
            // parallel with the same app password; on a cold cache they all load-modify-save
            // this same row, so a tracked SaveChanges made all-but-one lose the concurrency
            // race and surface as a bogus 412. LastUsedAt is last-writer-wins bookkeeping, so
            // a raw UPDATE is correct and race-free (SQLite + Postgres, ADR 0001).
            await dbContext.AppPasswords
                .Where(p => p.Id == appPassword.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LastUsedAt, clock.UtcNow), cancellationToken);

            var identity = new DavIdentity(user.Id, user.TenantId, user.Email, appPassword.Id);
            cache.Set(cacheKey, identity, CacheTtl);
            return identity;
        }

        return null;
    }
}
