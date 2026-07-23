using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A calendar user's busy windows over a range (ADR 0030).</summary>
public sealed class FreeBusyResource : HypermediaResource
{
    public required string Address { get; init; }

    public required DateTime FromUtc { get; init; }

    public required DateTime ToUtc { get; init; }

    /// <summary>False when the address didn't resolve to a local user (no availability known).</summary>
    public required bool Resolved { get; init; }

    public IReadOnlyList<BusyPeriodResource> Busy { get; init; } = [];
}

public sealed class BusyPeriodResource
{
    public required DateTime StartUtc { get; init; }

    public required DateTime EndUtc { get; init; }
}
