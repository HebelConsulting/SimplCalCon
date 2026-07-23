using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Application.Abstractions.Security;
using SimplCalCon.Domain.Authentication;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;

namespace SimplCalCon.Infrastructure.Identity;

internal sealed class AccountActivationService(
    SimplCalConDbContext dbContext,
    PasswordHashing passwordHashing,
    IPasswordPolicy passwordPolicy,
    IClock clock) : IAccountActivationService
{
    private static readonly TimeSpan ActivationLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(2);

    public async Task<IssuedToken> IssueAsync(
        Guid userId, TokenPurpose purpose, Guid issuedByPrincipalId, CancellationToken cancellationToken)
    {
        var raw = SecretGenerator.Create();
        var token = new Token
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = TokenHashing.Hash(raw),
            Purpose = purpose,
            ExpiresAt = clock.UtcNow + (purpose == TokenPurpose.Activation ? ActivationLifetime : ResetLifetime),
            IssuedByPrincipalId = issuedByPrincipalId,
        };

        dbContext.Tokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedToken(token, raw);
    }

    public async Task<TokenRedemptionStatus> RedeemAsync(
        string rawToken, string newPassword, CancellationToken cancellationToken)
    {
        var hash = TokenHashing.Hash(rawToken);
        var token = await dbContext.Tokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return TokenRedemptionStatus.NotFound;
        }

        if (token.ConsumedAt is not null)
        {
            return TokenRedemptionStatus.AlreadyConsumed;
        }

        if (token.ExpiresAt <= clock.UtcNow)
        {
            return TokenRedemptionStatus.Expired;
        }

        if (!passwordPolicy.Validate(newPassword).IsAcceptable)
        {
            return TokenRedemptionStatus.PasswordRejected;
        }

        var user = await dbContext.Users.FirstAsync(u => u.Id == token.UserId, cancellationToken);
        user.PasswordHash = passwordHashing.Hash(newPassword);
        user.Status = UserStatus.Active;
        user.SecurityStamp = Guid.NewGuid();
        token.ConsumedAt = clock.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return TokenRedemptionStatus.Success;
    }
}
