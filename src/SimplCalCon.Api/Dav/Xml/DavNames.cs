using System.Xml.Linq;

namespace SimplCalCon.Api.Dav.Xml;

/// <summary>XML namespaces and element names for WebDAV / CardDAV (ADR 0003).</summary>
public static class DavNames
{
    public static readonly XNamespace Dav = "DAV:";
    public static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";
    public static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    // DAV: core
    public static readonly XName Multistatus = Dav + "multistatus";
    public static readonly XName Response = Dav + "response";
    public static readonly XName Href = Dav + "href";
    public static readonly XName Propstat = Dav + "propstat";
    public static readonly XName Prop = Dav + "prop";
    public static readonly XName Status = Dav + "status";
    public static readonly XName Propfind = Dav + "propfind";
    public static readonly XName AllProp = Dav + "allprop";
    public static readonly XName PropName = Dav + "propname";

    public static readonly XName ResourceType = Dav + "resourcetype";
    public static readonly XName Collection = Dav + "collection";
    public static readonly XName Principal = Dav + "principal";
    public static readonly XName DisplayName = Dav + "displayname";
    public static readonly XName GetEtag = Dav + "getetag";
    public static readonly XName GetContentType = Dav + "getcontenttype";
    public static readonly XName GetContentLength = Dav + "getcontentlength";
    public static readonly XName CurrentUserPrincipal = Dav + "current-user-principal";
    public static readonly XName PrincipalUrl = Dav + "principal-URL";
    public static readonly XName Owner = Dav + "owner";
    public static readonly XName SupportedReportSet = Dav + "supported-report-set";
    public static readonly XName SupportedReport = Dav + "supported-report";
    public static readonly XName Report = Dav + "report";
    public static readonly XName SyncToken = Dav + "sync-token";
    public static readonly XName SyncCollection = Dav + "sync-collection";
    public static readonly XName SyncLevel = Dav + "sync-level";
    public static readonly XName CurrentUserPrivilegeSet = Dav + "current-user-privilege-set";
    public static readonly XName Privilege = Dav + "privilege";
    public static readonly XName Read = Dav + "read";
    public static readonly XName Write = Dav + "write";
    public static readonly XName WriteContent = Dav + "write-content";
    public static readonly XName WriteProperties = Dav + "write-properties";
    public static readonly XName Bind = Dav + "bind";
    public static readonly XName Unbind = Dav + "unbind";

    // CardDAV
    public static readonly XName AddressBook = CardDav + "addressbook";
    public static readonly XName AddressBookHomeSet = CardDav + "addressbook-home-set";
    public static readonly XName AddressBookQuery = CardDav + "addressbook-query";
    public static readonly XName AddressBookMultiget = CardDav + "addressbook-multiget";
    public static readonly XName SupportedAddressData = CardDav + "supported-address-data";
    public static readonly XName AddressDataType = CardDav + "address-data-type";
    public static readonly XName AddressData = CardDav + "address-data";

    // calendarserver.org (CTag)
    public static readonly XName GetCTag = CalendarServer + "getctag";

    public const string Ok = "HTTP/1.1 200 OK";
    public const string NotFound = "HTTP/1.1 404 Not Found";
}
