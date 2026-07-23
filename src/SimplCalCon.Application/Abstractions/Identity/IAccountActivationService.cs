using SimplCalCon.Domain.Authentication;

namespace SimplCalCon.Application.Abstractions.Identity;

/// <summary>
/// Issues and redeems single-use activation / password-reset tokens (ADR 0016). The
/// raw token is returned once to the issuer for out-of-band delivery (SMTP is
/// Phase 3); only its hash is stored.
/// </summary>
public interface IAccountActivationService
{
    Task<IssuedToken> IssueAsync(Guid userId, TokenPurpose purpose, Guid issuedByPrincipalId, CancellationToken cancellationToken);

    Task<TokenRedemptionStatus> RedeemAsync(string rawToken, string newPassword, CancellationToken cancellationToken);
}

/// <summary>A freshly created token and its one-time clear-text value.</summary>
public sealed record IssuedToken(Token Token, string RawToken);

public enum TokenRedemptionStatus
{
    Success,
    NotFound,
    Expired,
    AlreadyConsumed,
    PasswordRejected,
}
