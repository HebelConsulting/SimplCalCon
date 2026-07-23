using System.Text.Json.Serialization;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A calendar event (extracted fields; the raw blob is available via DAV).</summary>
public sealed class EventResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public string? Summary { get; init; }

    public DateTime? StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public bool IsAllDay { get; init; }

    public bool IsRecurring { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}

/// <summary>The result of splitting an event: the truncated original and the newly created tail copy (ADR 0027).</summary>
public sealed class SplitEventResource : HypermediaResource
{
    public required EventResource Original { get; init; }

    public required EventResource Created { get; init; }
}

/// <summary>A contact (extracted fields).</summary>
public sealed class ContactResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public string? FormattedName { get; init; }

    public string? FamilyName { get; init; }

    public string? GivenName { get; init; }

    public string? Organization { get; init; }

    public IReadOnlyList<string> Emails { get; init; } = [];

    public IReadOnlyList<string> Phones { get; init; } = [];

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}
