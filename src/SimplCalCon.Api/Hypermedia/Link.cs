namespace SimplCalCon.Api.Hypermedia;

/// <summary>A single hypermedia link (ADR 0009).</summary>
public sealed class Link(string rel, string href, string method = "GET")
{
    public string Rel { get; } = rel;

    public string Href { get; } = href;

    public string Method { get; } = method;
}
