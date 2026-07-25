namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// A user's personal per-collection colour overrides (ADR 0066), layered over the owner-set colour.
/// </summary>
public interface IUserCollectionColorService
{
    /// <summary>The caller's colour overrides for the given collections, keyed by collection id (absent = none).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetOverridesAsync(
        Guid userId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken);

    /// <summary>The caller's colour override for one collection, or null.</summary>
    Task<string?> GetOverrideAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Sets (or replaces) the caller's colour override for a collection.</summary>
    Task SetAsync(Guid userId, Guid collectionId, string color, CancellationToken cancellationToken);

    /// <summary>Removes the caller's colour override (revert to the collection default).</summary>
    Task ClearAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken);
}
