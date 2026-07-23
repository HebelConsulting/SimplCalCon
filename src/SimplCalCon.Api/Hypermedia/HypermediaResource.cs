namespace SimplCalCon.Api.Hypermedia;

/// <summary>
/// Base for every API response resource: carries its hypermedia <see cref="Links"/>
/// (ADR 0009). Concrete resources add their own properties.
/// </summary>
public abstract class HypermediaResource
{
    public List<Link> Links { get; init; } = [];
}
