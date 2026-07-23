using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Authentication;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;

namespace SimplCalCon.Infrastructure.Identity;

internal sealed class AppPasswordService(
    SimplCalConDbContext dbContext,
    PasswordHashing passwordHashing,
    IClock clock) : IAppPasswordService
{
    public async Task<IssuedAppPassword> IssueAsync(Guid userId, string label, CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new InvalidOperationException($"Cannot issue an app password for unknown user '{userId}'.");
        }

        var secret = SecretGenerator.Create();
        var appPassword = new AppPassword
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = label,
            PasswordHash = passwordHashing.Hash(secret),
            CreatedAt = clock.UtcNow,
        };

        dbContext.AppPasswords.Add(appPassword);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedAppPassword(appPassword, secret);
    }

    public async Task<IReadOnlyList<AppPassword>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Order client-side: SQLite can't ORDER BY a DateTimeOffset (Postgres can).
        // The per-user set is tiny, so this is cheap; broader DateTimeOffset ordering
        // (e.g. calendar queries) will need a sortable stored representation.
        var appPasswords = await dbContext.AppPasswords
            .Where(a => a.UserId == userId && a.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return appPasswords.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<AppPassword?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await dbContext.AppPasswords
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId && a.RevokedAt == null, cancellationToken);

    public async Task<bool> RevokeAsync(
        Guid userId, Guid id, Guid? expectedConcurrencyToken, CancellationToken cancellationToken)
    {
        var appPassword = await dbContext.AppPasswords
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId && a.RevokedAt == null, cancellationToken);

        if (appPassword is null)
        {
            return false;
        }

        if (expectedConcurrencyToken is { } token)
        {
            // Textbook EF concurrency: a stale token makes the UPDATE affect zero rows
            // and throw DbUpdateConcurrencyException, which the Api maps to 412 (ADR 0009).
            dbContext.Entry(appPassword).Property(a => a.ConcurrencyToken).OriginalValue = token;
        }

        appPassword.RevokedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
