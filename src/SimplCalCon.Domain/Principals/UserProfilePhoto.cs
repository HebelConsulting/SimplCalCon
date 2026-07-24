using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Domain.Principals;

/// <summary>
/// A user's profile photo, stored as a normalized 256×256 PNG. A 1:1 shared-primary-key
/// companion to <see cref="User"/> (<see cref="UserId"/> is both PK and FK) so the
/// frequently-queried user row never carries the blob, and deleting the user cascades the
/// photo away. Clients normalize the image; the server only byte-guards it (ADR 0035).
/// </summary>
public class UserProfilePhoto
{
    /// <summary>The owning user — this is both the primary key and the FK to <see cref="User"/>.</summary>
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Tenant scope (null for platform admins), mirroring the owner's tenant.</summary>
    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>The normalized 256×256 PNG bytes.</summary>
    public required byte[] Photo { get; set; }

    /// <summary>Last write, used to cache-bust the rendered image.</summary>
    public DateTime UpdatedAt { get; set; }
}
