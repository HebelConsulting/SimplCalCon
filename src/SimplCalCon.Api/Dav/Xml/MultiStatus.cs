using System.Xml.Linq;

namespace SimplCalCon.Api.Dav.Xml;

/// <summary>Builds a WebDAV 207 Multi-Status document (RFC 4918 §13).</summary>
public static class MultiStatus
{
    public static XDocument Build(PropRequest request, IEnumerable<DavResource> resources)
    {
        var root = new XElement(
            DavNames.Multistatus,
            new XAttribute(XNamespace.Xmlns + "d", DavNames.Dav.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "card", DavNames.CardDav.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cs", DavNames.CalendarServer.NamespaceName));

        foreach (var resource in resources)
        {
            root.Add(BuildResponse(request, resource));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    /// <summary>Adds a top-level sync-token (used by sync-collection responses).</summary>
    public static XDocument WithSyncToken(XDocument document, string syncToken)
    {
        document.Root!.Add(new XElement(DavNames.SyncToken, syncToken));
        return document;
    }

    private static XElement BuildResponse(PropRequest request, DavResource resource)
    {
        var response = new XElement(DavNames.Response, new XElement(DavNames.Href, resource.Href));

        var (found, missing) = Select(request, resource);

        if (found.Count > 0 || (missing.Count == 0 && !request.PropName))
        {
            response.Add(new XElement(
                DavNames.Propstat,
                new XElement(DavNames.Prop, found),
                new XElement(DavNames.Status, DavNames.Ok)));
        }

        if (missing.Count > 0)
        {
            response.Add(new XElement(
                DavNames.Propstat,
                new XElement(DavNames.Prop, missing.Select(name => new XElement(name))),
                new XElement(DavNames.Status, DavNames.NotFound)));
        }

        return response;
    }

    private static (List<XElement> Found, List<XName> Missing) Select(PropRequest request, DavResource resource)
    {
        if (request.AllProp)
        {
            return (resource.Properties.Values.Select(v => new XElement(v)).ToList(), []);
        }

        if (request.PropName)
        {
            return (resource.Properties.Keys.Select(name => new XElement(name)).ToList(), []);
        }

        var found = new List<XElement>();
        var missing = new List<XName>();
        foreach (var name in request.Names)
        {
            if (resource.Properties.TryGetValue(name, out var value))
            {
                found.Add(new XElement(value));
            }
            else
            {
                missing.Add(name);
            }
        }

        return (found, missing);
    }
}
