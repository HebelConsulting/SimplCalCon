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

    public string? Color { get; init; }

    public bool SupportsEvents { get; init; }

    public bool SupportsTasks { get; init; }

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

    public string? Color { get; init; }

    public bool Shared { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}
