using SimplCalCon.Domain.Common;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Domain.Authentication;

/// <summary>
/// A per-device credential a user creates for DAV Basic authentication: named,
/// individually revocable, hashed at rest, and usable only on the DAV surface —
/// never the account password. Grants full DAV access as its owning user (no
/// per-collection scoping in v1). See docs/adr/0005-authentication.md.
/// </summary>
public class AppPassword : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>User-chosen label identifying the device, e.g. "iPhone".</summary>
    public required string Label { get; set; }

    /// <summary>Slow hash of the generated secret, used for cold verification.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last successful DAV authentication with this credential; null until first use.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when revoked; a revoked credential never authenticates.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
