using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using Calendar = SimplCalCon.Domain.Collections.Calendar;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>A calendar collection: PROPFIND, REPORT (calendar-query/multiget/sync-collection), MKCALENDAR/MKCOL, DELETE.</summary>
public sealed class CalDavCollectionController(
    IDavRepository repository, IFreeBusyService freeBusy, IAclService acl) : DavControllerBase
{
    private static readonly string[] IcalDateFormats =
        ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"];

    [HttpPropfind("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> Propfind(Guid userId, string cal, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        if (!await HasAccessAsync(calendar, AclRight.Read, acl, cancellationToken))
        {
            return ForbidDav();
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var rights = await EffectiveRightsAsync(calendar, acl, cancellationToken);
        var resources = new List<DavResource>
        {
            CalDavResources.CalendarCollection(CalendarHref(userId, cal), PrincipalHref(userId), calendar, rights),
        };

        if (Depth() >= 1)
        {
            foreach (var o in await repository.ListCalendarObjectsAsync(calendar.Id, cancellationToken))
            {
                resources.Add(CalDavResources.CalendarObjectResource(CalendarObjectHref(userId, cal, o.ResourceName), o));
            }
        }

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }

    [HttpProppatch("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> Proppatch(Guid userId, string cal, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        if (!await HasAccessAsync(calendar, AclRight.WriteContent, acl, cancellationToken))
        {
            return ForbidDav();
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        return DavXml.MultiStatus(MultiStatus.PropPatchAccepted(CalendarHref(userId, cal), body));
    }

    [HttpReport("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> Report(Guid userId, string cal, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        if (!await HasAccessAsync(calendar, AclRight.Read, acl, cancellationToken))
        {
            return ForbidDav();
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        if (body is null)
        {
            return BadRequest();
        }

        return body.Name switch
        {
            var n when n == DavNames.SyncCollection => await SyncCollectionAsync(userId, cal, calendar, body, cancellationToken),
            var n when n == DavNames.CalendarMultiget => await MultigetAsync(userId, cal, calendar, body, cancellationToken),
            var n when n == DavNames.CalendarQuery => await QueryAsync(userId, cal, calendar, body, cancellationToken),
            var n when n == DavNames.FreeBusyQuery => await FreeBusyAsync(calendar, body, cancellationToken),
            _ => BadRequest(),
        };
    }

    // RFC 4791 free-busy-query: return one VFREEBUSY for the calendar's owner over the time-range (ADR 0030).
    private async Task<IActionResult> FreeBusyAsync(Calendar calendar, XElement body, CancellationToken cancellationToken)
    {
        var range = body.Descendants(DavNames.TimeRange).FirstOrDefault();
        var from = ParseIcalUtc(range?.Attribute("start")?.Value) ?? DateTime.UtcNow;
        var to = ParseIcalUtc(range?.Attribute("end")?.Value) ?? from.AddDays(7);
        var busy = await freeBusy.GetBusyAsync(calendar.OwnerId, from, to, cancellationToken);
        return Content(FreeBusyDocument.Build(from, to, busy), "text/calendar");
    }

    [HttpMkcalendar("~/dav/calendars/{userId:guid}/{cal}")]
    [HttpMkcol("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> Create(Guid userId, string cal, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        if (CurrentTenantId is not { } tenantId)
        {
            return Forbid(SimplCalCon.Api.Authentication.DavAuthenticationDefaults.Scheme);
        }

        if (await repository.GetCalendarAsync(userId, cal, cancellationToken) is not null)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        var displayName = body?.Descendants(DavNames.DisplayName).FirstOrDefault()?.Value;
        var components = body?.Descendants(DavNames.Comp).Select(c => c.Attribute("name")?.Value).ToList() ?? [];
        var supportsEvents = components.Count == 0 || components.Contains("VEVENT");
        var supportsTasks = components.Count == 0 || components.Contains("VTODO");

        await repository.CreateCalendarAsync(userId, tenantId, cal, displayName, supportsEvents, supportsTasks, cancellationToken);
        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpDelete("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> Delete(Guid userId, string cal, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        return await repository.DeleteCalendarAsync(userId, cal, cancellationToken) ? NoContent() : NotFound();
    }

    private async Task<IActionResult> SyncCollectionAsync(
        Guid userId, string cal, Calendar calendar, XElement body, CancellationToken cancellationToken)
    {
        var tokenText = body.Element(DavNames.SyncToken)?.Value;
        long? sinceToken;
        if (string.IsNullOrEmpty(tokenText))
        {
            sinceToken = null;
        }
        else if (DavTokens.TryParse(tokenText) is { } parsed)
        {
            sinceToken = parsed;
        }
        else
        {
            return InvalidSyncToken();
        }

        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var result = await repository.SyncCalendarAsync(calendar.Id, sinceToken, cancellationToken);

        var changed = result.Changed
            .Select(o => CalDavResources.CalendarObjectResource(CalendarObjectHref(userId, cal, o.ResourceName), o))
            .ToList();

        var document = MultiStatus.Build(request, changed);
        foreach (var removed in result.RemovedResourceNames)
        {
            document.Root!.Add(new XElement(
                DavNames.Response,
                new XElement(DavNames.Href, CalendarObjectHref(userId, cal, removed)),
                new XElement(DavNames.Status, DavNames.NotFound)));
        }

        MultiStatus.WithSyncToken(document, DavTokens.Format(result.Token));
        return DavXml.MultiStatus(document);
    }

    private async Task<IActionResult> MultigetAsync(
        Guid userId, string cal, Calendar calendar, XElement body, CancellationToken cancellationToken)
    {
        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var names = body.Elements(DavNames.Href).Select(h => LastSegment(h.Value)).ToList();
        var found = (await repository.GetCalendarObjectsAsync(calendar.Id, names, cancellationToken))
            .ToDictionary(o => o.ResourceName);

        var document = MultiStatus.Build(request, []);
        foreach (var name in names)
        {
            if (found.TryGetValue(name, out var o))
            {
                var built = MultiStatus.Build(request, [
                    CalDavResources.CalendarObjectResource(CalendarObjectHref(userId, cal, name), o)]);
                document.Root!.Add(built.Root!.Elements(DavNames.Response));
            }
            else
            {
                document.Root!.Add(new XElement(
                    DavNames.Response,
                    new XElement(DavNames.Href, CalendarObjectHref(userId, cal, name)),
                    new XElement(DavNames.Status, DavNames.NotFound)));
            }
        }

        return DavXml.MultiStatus(document);
    }

    private async Task<IActionResult> QueryAsync(
        Guid userId, string cal, Calendar calendar, XElement body, CancellationToken cancellationToken)
    {
        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var filter = DavFilterParser.ParseCalendarQuery(body);

        var objects = await repository.QueryCalendarObjectsAsync(calendar.Id, filter, cancellationToken);
        var resources = objects
            .Select(o => CalDavResources.CalendarObjectResource(CalendarObjectHref(userId, cal, o.ResourceName), o))
            .ToList();

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }

    private IActionResult InvalidSyncToken()
    {
        var error = new XDocument(new XElement(
            DavNames.Dav + "error",
            new XAttribute(XNamespace.Xmlns + "d", DavNames.Dav.NamespaceName),
            new XElement(DavNames.Dav + "valid-sync-token")));

        return new ContentResult
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "application/xml; charset=utf-8",
            Content = DavXml.Serialize(error),
        };
    }

    private static DateTime? ParseIcalUtc(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            && DateTime.TryParseExact(value, IcalDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    private static string LastSegment(string href) => href.TrimEnd('/').Split('/').Last();
}
