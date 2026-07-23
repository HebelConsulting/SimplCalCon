using System.Text;
using System.Xml.Linq;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Api.Dav;

/// <summary>Builds the DAV property sets for each CardDAV resource type (ADR 0003).</summary>
internal static class CardDavResources
{
    public static DavResource Principal(
        string principalHref, string addressBookHomeHref, string calendarHomeHref, string displayName)
    {
        var resource = new DavResource(principalHref);
        resource.Set(DavNames.ResourceType, new XElement(DavNames.Principal));
        resource.Set(DavNames.DisplayName, displayName);
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.PrincipalUrl, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.AddressBookHomeSet, new XElement(DavNames.Href, addressBookHomeHref));
        resource.Set(DavNames.CalendarHomeSet, new XElement(DavNames.Href, calendarHomeHref));
        return resource;
    }

    public static DavResource Home(string homeHref, string principalHref)
    {
        var resource = new DavResource(homeHref);
        resource.Set(DavNames.ResourceType, new XElement(DavNames.Collection));
        resource.Set(DavNames.DisplayName, "Address Books");
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, principalHref));
        return resource;
    }

    public static DavResource AddressBookCollection(
        string collectionHref, string principalHref, AddressBook book, AclRight callerRights)
    {
        var resource = new DavResource(collectionHref);
        resource.Set(DavNames.ResourceType, new object[]
        {
            new XElement(DavNames.Collection),
            new XElement(DavNames.AddressBook),
        });
        resource.Set(DavNames.DisplayName, book.Name);
        resource.Set(DavNames.GetCTag, book.ChangeSequence.ToString());
        resource.Set(DavNames.SyncToken, DavTokens.Format(book.ChangeSequence));
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.Owner, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.SupportedReportSet, SupportedReports());
        resource.Set(DavNames.CurrentUserPrivilegeSet, DavPrivileges.From(callerRights));
        resource.Set(DavNames.SupportedAddressData, new[]
        {
            AddressDataType("3.0"),
            AddressDataType("4.0"),
        });
        return resource;
    }

    public static DavResource ContactObjectResource(string href, ContactObject contact)
    {
        var resource = new DavResource(href);
        resource.SetEmpty(DavNames.ResourceType);
        resource.Set(DavNames.GetEtag, ETag.Format(contact.ConcurrencyToken));
        resource.Set(DavNames.GetContentType, "text/vcard; charset=utf-8");
        resource.Set(DavNames.GetContentLength, Encoding.UTF8.GetByteCount(contact.Blob).ToString());
        resource.Set(DavNames.AddressData, contact.Blob);
        return resource;
    }

    private static IEnumerable<XElement> SupportedReports() =>
    [
        SupportedReport(DavNames.AddressBookQuery),
        SupportedReport(DavNames.AddressBookMultiget),
        SupportedReport(DavNames.SyncCollection),
    ];

    private static XElement SupportedReport(XName report) =>
        new(DavNames.SupportedReport, new XElement(DavNames.Report, new XElement(report)));

    private static XElement AddressDataType(string version) =>
        new(DavNames.AddressDataType, new XAttribute("content-type", "text/vcard"), new XAttribute("version", version));
}
