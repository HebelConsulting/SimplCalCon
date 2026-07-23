namespace SimplCalCon.Domain.Principals;

/// <summary>
/// A named, tenant-scoped set of principals usable as an ACL grant target (ADR 0007).
/// Groups may nest: a member can be a <see cref="User"/> or another
/// <see cref="Group"/>; membership cycles are rejected by the DbContext.
/// </summary>
public class Group : Principal
{
    /// <summary>
    /// Upper-invariant form of <see cref="Principal.DisplayName"/> backing
    /// case-insensitive uniqueness within the tenant (ADR 0001 provider parity).
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>Direct memberships (members may themselves be groups).</summary>
    public ICollection<GroupMembership> Memberships { get; set; } = [];
}
