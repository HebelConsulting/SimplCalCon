using System.ComponentModel.DataAnnotations;
using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A grant on a collection to a principal (a "share").</summary>
public sealed class ShareResource : HypermediaResource
{
    public required Guid PrincipalId { get; init; }

    public required string Kind { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public required IReadOnlyList<string> Rights { get; init; }
}

/// <summary>Sets the rights granted to a principal (kebab-case: read, write-content, create, delete, share, admin).</summary>
public sealed class ShareWriteRequest
{
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<string> Rights { get; init; }
}

/// <summary>A user or group in the caller's tenant (grantee picker).</summary>
public sealed class PrincipalResource
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }
}
