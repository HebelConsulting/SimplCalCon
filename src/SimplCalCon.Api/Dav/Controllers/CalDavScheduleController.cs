using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>
/// RFC 6638 scheduling collections (ADR 0030): the schedule-inbox/outbox are advertised so
/// native clients discover scheduling, and the outbox answers free-busy POSTs. Invitation
/// delivery into the inbox is a later slice.
/// </summary>
public sealed class CalDavScheduleController(IFreeBusyService freeBusy) : DavControllerBase
{
    private static readonly string[] IcalDateFormats =
        ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"];

    [HttpPropfind("~/dav/calendars/{userId:guid}/outbox")]
    public async Task<IActionResult> PropfindOutbox(Guid userId, CancellationToken cancellationToken) =>
        await ScheduleCollectionAsync(userId, "outbox", DavNames.ScheduleOutbox, cancellationToken);

    [HttpPropfind("~/dav/calendars/{userId:guid}/inbox")]
    public async Task<IActionResult> PropfindInbox(Guid userId, CancellationToken cancellationToken) =>
        await ScheduleCollectionAsync(userId, "inbox", DavNames.ScheduleInbox, cancellationToken);

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
