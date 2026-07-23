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
}
