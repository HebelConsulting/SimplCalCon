using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Scheduling;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>
/// RFC 6638 scheduling collections (ADR 0030/0031): the schedule-outbox answers free-busy
/// POSTs; the schedule-inbox is a functional collection that receives delivered iTIP
/// messages (REQUEST/REPLY/CANCEL) — native clients PROPFIND/sync/GET/DELETE it.
/// </summary>
public sealed class CalDavScheduleController(
    IFreeBusyService freeBusy, IScheduleInboxRepository inboxes) : DavControllerBase
{
    private static readonly string[] IcalDateFormats =
        ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"];

    [HttpPropfind("~/dav/calendars/{userId:guid}/outbox")]
    public async Task<IActionResult> PropfindOutbox(Guid userId, CancellationToken cancellationToken) =>
        await ScheduleCollectionAsync(userId, "outbox", DavNames.ScheduleOutbox, cancellationToken);

    [HttpPropfind("~/dav/calendars/{userId:guid}/inbox")]
    public async Task<IActionResult> PropfindInbox(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        if (CurrentTenantId is not { } tenantId)
        {
            return ForbidDav();
        }

        var inbox = await inboxes.EnsureInboxAsync(userId, tenantId, cancellationToken);
        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        var resources = new List<DavResource> { InboxResource(userId, inbox.ChangeSequence) };
        if (Depth() >= 1)
        {
            foreach (var message in await inboxes.ListMessagesAsync(inbox.Id, cancellationToken))
            {
                resources.Add(MessageResource(userId, message));
            }
        }

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }

    [HttpReport("~/dav/calendars/{userId:guid}/inbox")]
    public async Task<IActionResult> ReportInbox(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        if (CurrentTenantId is not { } tenantId)
        {
            return ForbidDav();
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        if (body is null || body.Name != DavNames.SyncCollection)
        {
            return BadRequest();
        }

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
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var inbox = await inboxes.EnsureInboxAsync(userId, tenantId, cancellationToken);
        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var result = await inboxes.SyncAsync(inbox.Id, sinceToken, cancellationToken);

        var document = MultiStatus.Build(request, result.Changed.Select(m => MessageResource(userId, m)));
        foreach (var removed in result.RemovedResourceNames)
        {
            document.Root!.Add(new XElement(
                DavNames.Response,
                new XElement(DavNames.Href, InboxMessageHref(userId, removed)),
                new XElement(DavNames.Status, DavNames.NotFound)));
        }

        MultiStatus.WithSyncToken(document, DavTokens.Format(result.Token));
        return DavXml.MultiStatus(document);
    }

    [HttpGet("~/dav/calendars/{userId:guid}/inbox/{name}")]
    public async Task<IActionResult> GetMessage(Guid userId, string name, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var inbox = await inboxes.GetInboxAsync(userId, cancellationToken);
        var message = inbox is null ? null : await inboxes.GetMessageAsync(inbox.Id, name, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        Response.Headers.ETag = ETag.Format(message.ConcurrencyToken);
        return Content(message.Blob, "text/calendar; charset=utf-8");
    }

    [HttpDelete("~/dav/calendars/{userId:guid}/inbox/{name}")]
    public async Task<IActionResult> DeleteMessage(Guid userId, string name, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var inbox = await inboxes.GetInboxAsync(userId, cancellationToken);
        return inbox is not null && await inboxes.DeleteMessageAsync(inbox.Id, name, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("~/dav/calendars/{userId:guid}/outbox")]
    public async Task<IActionResult> PostOutbox(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        if (CurrentTenantId is not { } tenantId)
        {
            return ForbidDav();
        }

        using var reader = new StreamReader(Request.Body);
        var request = await reader.ReadToEndAsync(cancellationToken);
        var lines = Unfold(request);

        var from = ParseIcalUtc(Value(lines, "DTSTART")) ?? DateTime.UtcNow;
        var to = ParseIcalUtc(Value(lines, "DTEND")) ?? from.AddDays(7);
        var organizer = Value(lines, "ORGANIZER");
        var recipients = lines
            .Where(l => l.StartsWith("ATTENDEE", StringComparison.OrdinalIgnoreCase))
            .Select(AddressOf)
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        var responses = new List<XElement>();
        foreach (var recipient in recipients)
        {
            var userIdForAddress = await freeBusy.ResolveUserAsync(tenantId, recipient, cancellationToken);
            var (status, data) = userIdForAddress is { } resolved
                ? ("2.0;Success", FreeBusyDocument.Build(
                    from, to, await freeBusy.GetBusyAsync(resolved, from, to, cancellationToken),
                    method: "REPLY", organizer: organizer, attendee: recipient))
                : ("3.7;Invalid calendar user", null);

            responses.Add(new XElement(DavNames.CalResponse,
                new XElement(DavNames.Recipient, new XElement(DavNames.Href, recipient)),
                new XElement(DavNames.RequestStatus, status),
                data is null ? null : new XElement(DavNames.CalendarData, data)));
        }

        var document = new XElement(DavNames.ScheduleResponse,
            new XAttribute(XNamespace.Xmlns + "D", DavNames.Dav.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "C", DavNames.CalDav.NamespaceName),
            responses);

        return Content(new XDocument(document).ToString(), "application/xml");
    }

    private DavResource InboxResource(Guid userId, long changeSequence)
    {
        var resource = new DavResource($"{CalendarHomeHref(userId)}inbox/");
        resource.Set(DavNames.ResourceType, new object[]
        {
            new XElement(DavNames.Collection),
            new XElement(DavNames.ScheduleInbox),
        });
        resource.Set(DavNames.DisplayName, "Inbox");
        resource.Set(DavNames.GetCTag, changeSequence.ToString());
        resource.Set(DavNames.SyncToken, DavTokens.Format(changeSequence));
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, PrincipalHref(userId)));
        resource.Set(DavNames.Owner, new XElement(DavNames.Href, PrincipalHref(userId)));
        resource.Set(DavNames.SupportedReportSet, new XElement(
            DavNames.SupportedReport, new XElement(DavNames.Report, new XElement(DavNames.SyncCollection))));
        return resource;
    }

    private DavResource MessageResource(Guid userId, ScheduleMessage message)
    {
        var resource = new DavResource(InboxMessageHref(userId, message.ResourceName));
        resource.Set(DavNames.GetEtag, ETag.Format(message.ConcurrencyToken));
        resource.Set(DavNames.GetContentType, "text/calendar; charset=utf-8");
        resource.Set(DavNames.CalendarData, message.Blob);
        return resource;
    }

    private string InboxMessageHref(Guid userId, string name) => $"{CalendarHomeHref(userId)}inbox/{name}";

    private async Task<IActionResult> ScheduleCollectionAsync(
        Guid userId, string segment, XName resourceType, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var resource = new DavResource($"{CalendarHomeHref(userId)}{segment}/");
        resource.Set(DavNames.ResourceType, new object[]
        {
            new XElement(DavNames.Collection),
            new XElement(resourceType),
        });
        resource.Set(DavNames.DisplayName, segment);
        resource.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, PrincipalHref(userId)));
        resource.Set(DavNames.Owner, new XElement(DavNames.Href, PrincipalHref(userId)));

        return DavXml.MultiStatus(MultiStatus.Build(request, [resource]));
    }

    // Minimal RFC 5545 line handling for the small free-busy request body.
    private static List<string> Unfold(string content)
    {
        var raw = content.Replace("\r\n", "\n").Split('\n');
        var lines = new List<string>();
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && lines.Count > 0)
            {
                lines[^1] += line[1..];
            }
            else if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static string? Value(List<string> lines, string property)
    {
        var line = lines.FirstOrDefault(l =>
            l.StartsWith($"{property}:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith($"{property};", StringComparison.OrdinalIgnoreCase));
        var colon = line?.IndexOf(':') ?? -1;
        return colon >= 0 ? line![(colon + 1)..] : null;
    }

    private static string? AddressOf(string line)
    {
        var colon = line.IndexOf(':');
        return colon >= 0 ? line[(colon + 1)..] : null;
    }

    private static DateTime? ParseIcalUtc(string? value) =>
        value is not null && DateTime.TryParseExact(
            value, IcalDateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
}
