using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>The calendar-home-set: lists the user's calendars (auto-provisioning a default).</summary>
public sealed class CalDavHomeController(IDavRepository repository) : DavControllerBase
{
    [AllowAnonymous]
    [HttpGet("~/.well-known/caldav")]
    [HttpPropfind("~/.well-known/caldav")]
    public IActionResult WellKnown()
    {
        Response.Headers.Location = "/dav/";
        return StatusCode(StatusCodes.Status301MovedPermanently);
    }

    [HttpPropfind("~/dav/calendars/{userId:guid}")]
    public async Task<IActionResult> Propfind(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        await repository.EnsureDefaultCalendarAsync(userId, CurrentTenantId, cancellationToken);

        var resources = new List<DavResource>
        {
            CalDavResources.Home(CalendarHomeHref(userId), PrincipalHref(userId)),
        };

        if (Depth() >= 1)
        {
            // Own calendars plus calendars shared with the user; a shared calendar is
            // rendered at its owner's URL (ADR 0007).
            foreach (var calendar in await repository.ListAccessibleCalendarsAsync(userId, cancellationToken))
            {
                resources.Add(CalDavResources.CalendarCollection(
                    CalendarHref(calendar.OwnerId, calendar.ResourceName), PrincipalHref(calendar.OwnerId), calendar));
            }
        }

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }
}
