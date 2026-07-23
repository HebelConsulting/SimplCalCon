namespace SimplCalCon.Api.Hypermedia;

/// <summary>
/// A hypermedia collection: the items plus collection-level links (and a home for
/// paging metadata later). ADR 0009.
/// </summary>
public sealed class CollectionResource<T> : HypermediaResource
{
    public required IReadOnlyList<T> Items { get; init; }
}
