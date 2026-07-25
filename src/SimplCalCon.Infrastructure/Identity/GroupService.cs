using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.Infrastructure.Identity;

/// <summary>Tenant-scoped group + membership management (ADR 0059); nesting cycles are rejected by the DbContext.</summary>
internal sealed class GroupService(
    SimplCalConDbContext dbContext, IClock clock, IPrincipalDirectory directory,
    IChangeNotifier changeNotifier, ILogger<GroupService> logger) : IGroupService
{
    public async Task<IReadOnlyList<GroupSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Groups
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.DisplayName)
            .Select(g => new GroupSummary(g.Id, g.DisplayName, dbContext.GroupMemberships.Count(m => m.GroupId == g.Id)))
            .ToListAsync(cancellationToken);

    public async Task<GroupSummary?> CreateAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        if (await dbContext.Groups.AnyAsync(g => g.TenantId == tenantId && g.NormalizedName == normalized, cancellationToken))
        {
            return null;
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = name.Trim(),
            NormalizedName = normalized,
            CreatedAt = clock.UtcNow,
        };
        dbContext.Groups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new GroupSummary(group.Id, group.DisplayName, 0);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken)
    {
        if (!await InTenantAsync(tenantId, groupId, cancellationToken))
        {
            return false;
        }

        // Drop memberships in both directions (this group's members, and its own membership of others).
        await dbContext.GroupMemberships
            .Where(m => m.GroupId == groupId || m.MemberId == groupId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GroupMemberSummary>> ListMembersAsync(
        Guid tenantId, Guid groupId, CancellationToken cancellationToken)
    {
        if (!await InTenantAsync(tenantId, groupId, cancellationToken))
        {
            return [];
        }

        var memberIds = await dbContext.GroupMemberships
            .Where(m => m.GroupId == groupId)
            .Select(m => m.MemberId)
            .ToListAsync(cancellationToken);

        return (await directory.GetAsync(memberIds, cancellationToken))
            .Select(p => new GroupMemberSummary(p.Id, p.Kind, p.DisplayName, p.Email))
            .OrderBy(p => p.DisplayName)
            .ToList();
    }

    public async Task<AddMemberResult> AddMemberAsync(
        Guid tenantId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        if (!await InTenantAsync(tenantId, groupId, cancellationToken) || !await PrincipalInTenantAsync(tenantId, memberId, cancellationToken))
        {
            return AddMemberResult.NotFound;
        }

        if (await dbContext.GroupMemberships.AnyAsync(m => m.GroupId == groupId && m.MemberId == memberId, cancellationToken))
        {
            return AddMemberResult.Added; // idempotent
        }

        dbContext.GroupMemberships.Add(new GroupMembership { GroupId = groupId, MemberId = memberId });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyMemberSharesChangedAsync(memberId, cancellationToken);
            return AddMemberResult.Added;
        }
        catch (InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return AddMemberResult.WouldCycle; // nesting cycle rejected by the DbContext invariant
        }
    }

    public async Task<bool> RemoveMemberAsync(Guid tenantId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        if (!await InTenantAsync(tenantId, groupId, cancellationToken))
        {
            return false;
        }

        var removed = await dbContext.GroupMemberships
            .Where(m => m.GroupId == groupId && m.MemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
        if (removed)
        {
            await NotifyMemberSharesChangedAsync(memberId, cancellationToken);
        }

        return removed;
    }

    // A membership change alters what the affected member (its transitive users) can access, so their
    // "shared with me" should reload (ADR 0064). Best-effort — never fails the membership change.
    private async Task NotifyMemberSharesChangedAsync(Guid memberId, CancellationToken cancellationToken)
    {
        try
        {
            var users = await PrincipalGraph.GetMemberUserIdsAsync(dbContext, memberId, cancellationToken);
            if (users.Count > 0)
            {
                await changeNotifier.SharesChangedAsync(users, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push shares-changed notification for member {MemberId}.", memberId);
        }
    }

    private async Task<bool> InTenantAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken) =>
        await dbContext.Groups.AnyAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken);

    private async Task<bool> PrincipalInTenantAsync(Guid tenantId, Guid principalId, CancellationToken cancellationToken) =>
        await dbContext.Users.AnyAsync(u => u.Id == principalId && u.TenantId == tenantId, cancellationToken)
        || await dbContext.Groups.AnyAsync(g => g.Id == principalId && g.TenantId == tenantId, cancellationToken);
}
