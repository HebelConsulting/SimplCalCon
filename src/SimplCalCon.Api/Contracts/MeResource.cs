using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>The authenticated user (<c>GET /api/me</c>).</summary>
public sealed class MeResource : HypermediaResource
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public Guid? TenantId { get; init; }

    public required string Role { get; init; }
}
