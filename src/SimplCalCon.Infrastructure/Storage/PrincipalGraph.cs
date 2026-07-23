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
}
