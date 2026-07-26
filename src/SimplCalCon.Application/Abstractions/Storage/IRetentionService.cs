namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Retention maintenance (ADR 0060): permanently purges trashed objects (and their revision history)
/// that were soft-deleted before a cutoff, across all collections. A hard delete like
/// <see cref="IObjectStore.PurgeAsync"/> — no change-sequence bump, since clients already saw the tombstone.
/// </summary>
public interface IRetentionService
{
    /// <summary>Purges up to <paramref name="batchSize"/> objects trashed before <paramref name="cutoffUtc"/>. Returns the count purged.</summary>
    Task<int> PurgeTrashedBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Hard-purges up to <paramref name="batchSize"/> collections soft-deleted before <paramref name="cutoffUtc"/>
    /// (their objects + revisions + child rows go via cascade, ADR 0077). Returns the count purged.
    /// </summary>
    Task<int> PurgeDeletedCollectionsBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Prunes old revision history (ADR 0080): for up to <paramref name="batchSize"/> objects, removes
    /// revisions older than <paramref name="cutoffUtc"/> that also fall outside the most-recent
    /// <paramref name="keepMinimum"/> per object (so the latest history is always retained). Returns the
    /// number of objects whose history was pruned this call.
    /// </summary>
    Task<int> PruneRevisionsAsync(DateTime cutoffUtc, int keepMinimum, int batchSize, CancellationToken cancellationToken);
}
