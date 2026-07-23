namespace SimplCalCon.Domain.Principals;

/// <summary>
/// A user's role within its own tenant. Platform administrators are modelled as
/// tenant-less users (<see cref="User.TenantId"/> is null) and carry no
/// <see cref="TenantRole"/>. See docs/adr/0006-multi-tenancy-and-administration.md.
/// </summary>
public enum TenantRole
{
    /// <summary>Ordinary member: owns and shares their own collections.</summary>
    Member = 0,

    /// <summary>Administers the tenant: manages users, groups, and tenant defaults.</summary>
    Admin = 1,
}
