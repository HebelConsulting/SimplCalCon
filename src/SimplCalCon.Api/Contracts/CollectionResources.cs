using System.Text.Json.Serialization;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>A calendar (<c>/api/calendars/{id}</c>).</summary>
public sealed class CalendarResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public required string Name { get; init; }

    /// <summary>The owner-set shared default colour (ADR 0062).</summary>
    public string? Color { get; init; }

    /// <summary>The caller's personal colour override (ADR 0066), if any.</summary>
    public string? MyColor { get; init; }

    public bool SupportsEvents { get; init; }

    public bool SupportsTasks { get; init; }

    /// <summary>The subscription-feed token (ADR 0069) — owner-only; null when disabled or not the owner.</summary>
    public string? FeedToken { get; init; }

    /// <summary>True when the calendar belongs to another user and is shared with the caller.</summary>
    public bool Shared { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}

/// <summary>An address book (<c>/api/address-books/{id}</c>).</summary>
public sealed class AddressBookResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public required string Name { get; init; }

    /// <summary>The owner-set shared default colour (ADR 0062).</summary>
    public string? Color { get; init; }

    /// <summary>The caller's personal colour override (ADR 0066), if any.</summary>
    public string? MyColor { get; init; }

    /// <summary>The subscription-feed token (ADR 0069) — owner-only; null when disabled or not the owner.</summary>
    public string? FeedToken { get; init; }

    public bool Shared { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}
