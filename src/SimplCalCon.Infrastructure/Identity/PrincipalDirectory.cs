using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Identity;

internal sealed class PrincipalDirectory(SimplCalConDbContext dbContext) : IPrincipalDirectory
{
    private const int MaxResults = 20;

    public async Task<IReadOnlyList<PrincipalSummary>> SearchAsync(
        Guid tenantId, string? query, CancellationToken cancellationToken)
    {
        var users = dbContext.Users.Where(u => u.TenantId == tenantId);
        var groups = dbContext.Groups.Where(g => g.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var upper = query.ToUpperInvariant();
            users = users.Where(u => u.NormalizedEmail.Contains(upper) || u.DisplayName.Contains(query));
            groups = groups.Where(g => g.NormalizedName.Contains(upper) || g.DisplayName.Contains(query));
        }

        var userSummaries = await users
            .OrderBy(u => u.DisplayName)
            .Take(MaxResults)
            .Select(u => new PrincipalSummary(u.Id, "User", u.DisplayName, u.Email))
            .ToListAsync(cancellationToken);

        var groupSummaries = await groups
            .OrderBy(g => g.DisplayName)
            .Take(MaxResults)
            .Select(g => new PrincipalSummary(g.Id, "Group", g.DisplayName, null))
            .ToListAsync(cancellationToken);

        return [.. userSummaries, .. groupSummaries];
    }

    public async Task<IReadOnlyList<PrincipalSummary>> GetAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.Principals
            .Where(p => ids.Contains(p.Id))
            .Select(p => new PrincipalSummary(
                p.Id,
                p is Group ? "Group" : "User",
                p.DisplayName,
                p is User ? ((User)p).Email : null))
            .ToListAsync(cancellationToken);
}
