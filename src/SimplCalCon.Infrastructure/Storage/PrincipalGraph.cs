using Microsoft.EntityFrameworkCore;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Resolves the ACL principal set for a user: the user plus every group they belong to, transitively (ADR 0007, 0016).</summary>
internal static class PrincipalGraph
{
    public static async Task<IReadOnlyList<Guid>> GetPrincipalIdsAsync(
        SimplCalConDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var principals = new HashSet<Guid> { userId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(userId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var containingGroups = await dbContext.GroupMemberships
                .Where(m => m.MemberId == current)
                .Select(m => m.GroupId)
                .ToListAsync(cancellationToken);

            foreach (var groupId in containingGroups)
            {
                if (principals.Add(groupId))
                {
                    frontier.Enqueue(groupId);
                }
            }
        }

        return [.. principals];
    }

    /// <summary>
    /// The user ids affected by a grant to <paramref name="principalId"/> (ADR 0064): the user itself,
    /// or — if it's a group — every user transitively a member of it (nested groups included).
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> GetMemberUserIdsAsync(
        SimplCalConDbContext dbContext, Guid principalId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(u => u.Id == principalId, cancellationToken))
        {
            return [principalId];
        }

        // Walk group memberships downward, collecting every reachable member principal.
        var reachable = new HashSet<Guid>();
        var visited = new HashSet<Guid> { principalId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(principalId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var members = await dbContext.GroupMemberships
                .Where(m => m.GroupId == current)
                .Select(m => m.MemberId)
                .ToListAsync(cancellationToken);

            foreach (var memberId in members)
            {
                reachable.Add(memberId);
                if (visited.Add(memberId)) // a member may itself be a group — expand it (a user has no rows here)
                {
                    frontier.Enqueue(memberId);
                }
            }
        }

        // Keep only the users among the reachable principals.
        return await dbContext.Users
            .Where(u => reachable.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }
}
