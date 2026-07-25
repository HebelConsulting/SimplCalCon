namespace SimplCalCon.Application.Abstractions.Acl;

/// <summary>
/// Tenant-scoped group management for the admin UI (ADR 0059): create/delete groups and manage
/// their members, so group-based ACL grants (ADR 0007) are usable. All operations are confined to
/// the given tenant; members must belong to the same tenant, and a membership that would create a
/// nesting cycle is rejected.
/// </summary>
public interface IGroupService
{
    Task<IReadOnlyList<GroupSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Creates a group; returns null if the tenant already has one with that (case-insensitive) name.</summary>
    Task<GroupSummary?> CreateAsync(Guid tenantId, string name, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GroupMemberSummary>> ListMembersAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Adds a same-tenant principal to the group (idempotent). Returns the outcome (added / cycle / not found).</summary>
    Task<AddMemberResult> AddMemberAsync(Guid tenantId, Guid groupId, Guid memberId, CancellationToken cancellationToken);

    Task<bool> RemoveMemberAsync(Guid tenantId, Guid groupId, Guid memberId, CancellationToken cancellationToken);
}

public sealed record GroupSummary(Guid Id, string Name, int MemberCount);

public sealed record GroupMemberSummary(Guid Id, string Kind, string DisplayName, string? Email);

public enum AddMemberResult
{
    Added,
    NotFound,
    WouldCycle,
}
