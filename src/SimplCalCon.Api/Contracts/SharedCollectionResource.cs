using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A collection another user has shared with the caller (ADR 0046).</summary>
public sealed class SharedCollectionResource : HypermediaResource
{
    public required Guid Id { get; init; }

    /// <summary>"calendars" or "address-books".</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string OwnerName { get; init; }

    /// <summary>The caller's effective rights on the collection (e.g. read, write-content, share).</summary>
    public IReadOnlyList<string> Rights { get; init; } = [];
}
