using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Purges trashed objects (+ their revisions) past the retention cutoff (ADR 0060), batched across all collections.</summary>
internal sealed class RetentionService(SimplCalConDbContext dbContext) : IRetentionService
{
    public async Task<int> PurgeTrashedBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Objects
            .Where(o => o.IsDeleted && o.DeletedAt != null && o.DeletedAt < cutoffUtc)
            .OrderBy(o => o.DeletedAt)
            .Take(batchSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ObjectRevisions.Where(r => ids.Contains(r.ObjectId)).ExecuteDeleteAsync(cancellationToken);
        var purged = await dbContext.Objects.Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purged;
    }

    public async Task<int> PurgeDeletedCollectionsBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Collections
            .Where(c => c.IsDeleted && c.DeletedAt != null && c.DeletedAt < cutoffUtc)
            .OrderBy(c => c.DeletedAt)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        // Delete the object subtree explicitly (deterministic, like the trash purge above); the remaining
        // small child tables (ACL entries, push subscriptions, per-user colours) go via FK cascade.
        var objectIds = await dbContext.Objects
            .Where(o => ids.Contains(o.CollectionId))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (objectIds.Count > 0)
        {
            await dbContext.ObjectRevisions.Where(r => objectIds.Contains(r.ObjectId)).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Objects.Where(o => objectIds.Contains(o.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        var purged = await dbContext.Collections.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purged;
    }

    public async Task<int> PruneRevisionsAsync(
        DateTime cutoffUtc, int keepMinimum, int batchSize, CancellationToken cancellationToken)
    {
        // Objects that still have prunable history: a revision older than the cutoff AND outside the
        // most-recent `keepMinimum` (RevisionNumber is a per-object monotonic counter on the object, so
        // "outside the last N" is `RevisionNumber <= counter - keepMinimum"). Correlated EXISTS in a
        // SELECT is provider-safe (unlike a correlated MAX inside ExecuteDelete).
        var candidates = await dbContext.Objects
            .Where(o => dbContext.ObjectRevisions.Any(r =>
                r.ObjectId == o.Id && r.CreatedAt < cutoffUtc && r.RevisionNumber <= o.RevisionNumber - keepMinimum))
            .OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.RevisionNumber })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            // Plain comparisons only (captured constants) — translates on both providers.
            var threshold = candidate.RevisionNumber - keepMinimum;
            await dbContext.ObjectRevisions
                .Where(r => r.ObjectId == candidate.Id && r.CreatedAt < cutoffUtc && r.RevisionNumber <= threshold)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return candidates.Count;
    }
}
