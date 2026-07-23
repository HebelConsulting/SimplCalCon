namespace SimplCalCon.Domain.Principals;

/// <summary>
/// A direct membership edge: <see cref="MemberId"/> (a user or a group) belongs to
/// <see cref="GroupId"/>. Nested memberships resolve transitively when computing
/// effective ACL rights (ADR 0007); the DbContext rejects edges that would form a
/// cycle.
/// </summary>
public class GroupMembership
{
    /// <summary>The containing group.</summary>
    public Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;

    /// <summary>The member principal — a <see cref="User"/> or a nested <see cref="Group"/>.</summary>
    public Guid MemberId { get; set; }

    public Principal Member { get; set; } = null!;
}
