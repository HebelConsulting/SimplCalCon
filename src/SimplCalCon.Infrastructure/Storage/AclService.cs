using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Acl.Exceptions;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

internal sealed class AclService(
    SimplCalConDbContext dbContext, IClock clock, IChangeNotifier changeNotifier, ILogger<AclService> logger) : IAclService
{
    private const AclRight AllRights =
        AclRight.Read | AclRight.WriteContent | AclRight.Create | AclRight.Delete | AclRight.Share | AclRight.Admin;

    public async Task GrantAsync(
        Guid collectionId, Guid principalId, AclRight rights, CancellationToken cancellationToken)
    {
        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

        var principal = await dbContext.Principals.FirstOrDefaultAsync(p => p.Id == principalId, cancellationToken)
            ?? throw new InvalidOperationException($"Unknown principal '{principalId}'.");

        if (principal.TenantId != collection.TenantId)
        {
            throw new CrossTenantGrantException();
        }

        var entry = await dbContext.AclEntries
            .FirstOrDefaultAsync(e => e.CollectionId == collectionId && e.PrincipalId == principalId, cancellationToken);

        if (entry is null)
        {
            entry = new AclEntry
            {
                Id = Guid.NewGuid(),
                CollectionId = collectionId,
                PrincipalId = principalId,
                CreatedAt = clock.UtcNow.UtcDateTime,
            };
            dbContext.AclEntries.Add(entry);
        }

        entry.Rights = rights;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifySharesChangedAsync(collectionId, principalId, cancellationToken);
    }

    public async Task RevokeAsync(Guid collectionId, Guid principalId, CancellationToken cancellationToken)
    {
        var entry = await dbContext.AclEntries
            .FirstOrDefaultAsync(e => e.CollectionId == collectionId && e.PrincipalId == principalId, cancellationToken);

        if (entry is not null)
        {
            dbContext.AclEntries.Remove(entry);
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifySharesChangedAsync(collectionId, principalId, cancellationToken);
        }
    }

    // Post-commit live-refresh signal (ADR 0064): the owner's "shared by me" and the affected
    // principals' "shared with me" may have changed. Best-effort — a push failure must not fail the grant.
    private async Task NotifySharesChangedAsync(Guid collectionId, Guid principalId, CancellationToken cancellationToken)
    {
        try
        {
            var affected = new HashSet<Guid>();
            var ownerId = await dbContext.Collections
                .Where(c => c.Id == collectionId)
                .Select(c => (Guid?)c.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (ownerId is { } owner)
            {
                affected.Add(owner);
            }

            foreach (var userId in await PrincipalGraph.GetMemberUserIdsAsync(dbContext, principalId, cancellationToken))
            {
                affected.Add(userId);
            }

            if (affected.Count > 0)
            {
                await changeNotifier.SharesChangedAsync(affected, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push shares-changed notification for collection {CollectionId}.", collectionId);
        }
    }

    public async Task<IReadOnlyList<AclEntry>> ListGrantsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.AclEntries
            .Where(e => e.CollectionId == collectionId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<AclRight> GetEffectiveRightsAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken)
    {
        var ownerId = await dbContext.Collections
            .Where(c => c.Id == collectionId && !c.IsDeleted)
            .Select(c => (Guid?)c.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            return AclRight.None;
        }

        if (ownerId == userId)
        {
            return AllRights;
        }

        var principalIds = await PrincipalGraph.GetPrincipalIdsAsync(dbContext, userId, cancellationToken);
        var grants = await dbContext.AclEntries
            .Where(e => e.CollectionId == collectionId && principalIds.Contains(e.PrincipalId))
            .Select(e => e.Rights)
            .ToListAsync(cancellationToken);

        return grants.Aggregate(AclRight.None, (accumulated, right) => accumulated | right);
    }
}
