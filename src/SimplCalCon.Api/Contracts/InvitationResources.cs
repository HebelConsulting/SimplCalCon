using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A pending calendar invitation in the user's schedule-inbox (ADR 0045).</summary>
public sealed class InvitationResource : HypermediaResource
{
    public required string ResourceName { get; init; }

    public required string Uid { get; init; }

    public string? Summary { get; init; }

    public DateTime? StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public required string OrganizerEmail { get; init; }

    public string? OrganizerName { get; init; }
}
