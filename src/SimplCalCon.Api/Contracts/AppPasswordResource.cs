using System.Text.Json.Serialization;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A per-device DAV app password. The secret is never included here.</summary>
public class AppPasswordResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string Label { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>Backs the ETag header only; never serialized into the body.</summary>
    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}

/// <summary>
/// The response to creating an app password: like <see cref="AppPasswordResource"/>
/// but carrying the one-time clear-text <see cref="Secret"/>, shown exactly once.
/// </summary>
public sealed class AppPasswordCreatedResource : AppPasswordResource
{
    public required string Secret { get; init; }
}
