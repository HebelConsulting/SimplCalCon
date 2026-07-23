using System.Text;
using System.Xml.Linq;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Api.Dav;

/// <summary>Builds the DAV property sets for CalDAV resources (ADR 0003, CalDAV).</summary>
internal static class CalDavResources
{
    public static DavResource Home(string homeHref, string principalHref)
    {
        var resource = new DavResource(homeHref);
        resource.Set(DavNames.ResourceType, new XElement(DavNames.Collection));
        resource.Set(DavNames.DisplayName, "Calendars");
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, principalHref));
        return resource;
    }

    public static DavResource CalendarCollection(
        string calendarHref, string principalHref, Calendar calendar, AclRight callerRights)
    {
        var resource = new DavResource(calendarHref);
        resource.Set(DavNames.ResourceType, new object[]
        {
            new XElement(DavNames.Collection),
            new XElement(DavNames.Calendar),
        });
        resource.Set(DavNames.DisplayName, calendar.Name);
        resource.Set(DavNames.GetCTag, calendar.ChangeSequence.ToString());
        resource.Set(DavNames.SyncToken, DavTokens.Format(calendar.ChangeSequence));
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.Owner, new XElement(DavNames.Href, principalHref));
        resource.Set(DavNames.SupportedReportSet, SupportedReports());
        resource.Set(DavNames.CurrentUserPrivilegeSet, DavPrivileges.From(callerRights));
        resource.Set(DavNames.SupportedCalendarComponentSet, SupportedComponents(calendar));
        resource.Set(DavNames.SupportedCalendarData,
            new XElement(DavNames.CalendarData,
                new XAttribute("content-type", "text/calendar"), new XAttribute("version", "2.0")));
        return resource;
    }

    public static DavResource CalendarObjectResource(string href, CalendarObject calendarObject)
    {
        var resource = new DavResource(href);
        resource.SetEmpty(DavNames.ResourceType);
        resource.Set(DavNames.GetEtag, ETag.Format(calendarObject.ConcurrencyToken));
        resource.Set(DavNames.GetContentType, "text/calendar; charset=utf-8");
        resource.Set(DavNames.GetContentLength, Encoding.UTF8.GetByteCount(calendarObject.Blob).ToString());
        resource.Set(DavNames.CalendarData, calendarObject.Blob);
        return resource;
    }

    private static IEnumerable<XElement> SupportedReports() =>
    [
        SupportedReport(DavNames.CalendarQuery),
        SupportedReport(DavNames.CalendarMultiget),
        SupportedReport(DavNames.SyncCollection),
    ];

    private static XElement SupportedReport(XName report) =>
        new(DavNames.SupportedReport, new XElement(DavNames.Report, new XElement(report)));

    private static IEnumerable<XElement> SupportedComponents(Calendar calendar)
    {
        if (calendar.SupportsEvents)
        {
            yield return new XElement(DavNames.Comp, new XAttribute("name", "VEVENT"));
        }

        if (calendar.SupportsTasks)
        {
            yield return new XElement(DavNames.Comp, new XAttribute("name", "VTODO"));
        }
    }
}
