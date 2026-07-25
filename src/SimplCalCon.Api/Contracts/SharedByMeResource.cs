using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>An owned collection the caller has shared, with its grants (ADR 0058).</summary>
public sealed class SharedByMeResource : HypermediaResource
{
    public required Guid Id { get; init; }

    /// <summary>"calendars" or "address-books".</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<ShareResource> Shares { get; init; }
}
