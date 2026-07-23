using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Domain.Authentication;

/// <summary>
/// A single-use, expiring secret for account activation or password reset. Only the
/// hash is stored; the raw token is shown once to the issuer, who delivers the link
/// out of band (SMTP delivery arrives in Phase 3). See docs/adr/0005 and docs/adr/0006.
/// </summary>
public class Token
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Hash of the raw token; the raw value is never persisted.</summary>
    public required string TokenHash { get; set; }

    public TokenPurpose Purpose { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when redeemed; a consumed token cannot be reused.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>The principal that issued this token (the admin who created the invite, etc.).</summary>
    public Guid IssuedByPrincipalId { get; set; }
}
