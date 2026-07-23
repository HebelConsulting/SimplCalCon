using System.ComponentModel.DataAnnotations;

namespace SimplCalCon.Api.Contracts;

/// <summary>Request body for creating an app password.</summary>
public sealed class AppPasswordCreateRequest
{
    /// <summary>Device label, e.g. "iPhone".</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Label { get; init; }
}
