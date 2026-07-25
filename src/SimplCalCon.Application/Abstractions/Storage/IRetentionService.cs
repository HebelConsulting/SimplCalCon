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
}
