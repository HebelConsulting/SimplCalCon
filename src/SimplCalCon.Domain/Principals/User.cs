using SimplCalCon.Domain.Authentication;

namespace SimplCalCon.Domain.Principals;

/// <summary>
/// A human account. Signs in to the web UI / REST with email + password (OIDC) and
/// syncs devices via per-device <see cref="AppPassword"/>s. See docs/adr/0005.
/// </summary>
public class User : Principal
{
    /// <summary>Login identifier, globally unique across the deployment (ADR 0006).</summary>
    public required string Email { get; set; }

    /// <summary>
    /// Upper-invariant form of <see cref="Email"/> backing case-insensitive
    /// uniqueness without relying on database collation (ADR 0001 provider parity).
    /// </summary>
    public required string NormalizedEmail { get; set; }

    /// <summary>Hashed account password; null until the account is activated.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Regenerated whenever credentials change or the account is disabled, to
    /// invalidate outstanding tokens and sessions.
    /// </summary>
    public Guid SecurityStamp { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Invited;

    /// <summary>Role within the owning tenant; null for a platform administrator (no tenant).</summary>
    public TenantRole? TenantRole { get; set; }

    /// <summary>True for a tenant-less platform administrator.</summary>
    public bool IsPlatformAdministrator => TenantId is null;

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    public ICollection<AppPassword> AppPasswords { get; set; } = [];
}
