using System.Xml.Linq;

namespace SimplCalCon.Api.Dav.Xml;

/// <summary>
/// One resource in a Multi-Status response: its href and the full set of properties it
/// can provide. <see cref="MultiStatus"/> selects from these per the PROPFIND request,
/// reporting requested-but-absent properties as 404.
/// </summary>
public sealed class DavResource(string href)
{
    public string Href { get; } = href;

    public Dictionary<XName, XElement> Properties { get; } = [];

    /// <summary>Adds a property whose content is text, an XElement, or a set of child elements.</summary>
    public DavResource Set(XName name, object? content)
    {
        Properties[name] = new XElement(name, content);
        return this;
    }

    /// <summary>Adds a valueless property element (e.g. a marker).</summary>
    public DavResource SetEmpty(XName name)
    {
        Properties[name] = new XElement(name);
        return this;
    }
}
