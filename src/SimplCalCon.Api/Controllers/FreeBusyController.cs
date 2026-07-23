using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Free/busy availability lookup for a calendar-user address in the caller's tenant
/// (ADR 0030): the same computation the CalDAV free-busy path uses, for the scheduling UI.
/// </summary>
[ApiController]
[Route("api/free-busy")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class FreeBusyController(IFreeBusyService freeBusy) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FreeBusyResource>> Get(
        [FromQuery] string address,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        if (to <= from)
        {
            return BadRequest("toUtc must be after fromUtc.");
        }

        var resolved = User.GetTenantId() is { } tenantId
            ? await freeBusy.ResolveUserAsync(tenantId, address, cancellationToken)
            : null;

        var busy = resolved is { } userId
            ? await freeBusy.GetBusyAsync(userId, from, to, cancellationToken)
            : [];

        return new FreeBusyResource
        {
            Address = address,
            FromUtc = from,
            ToUtc = to,
            Resolved = resolved is not null,
            Busy = busy.Select(b => new BusyPeriodResource { StartUtc = b.StartUtc, EndUtc = b.EndUtc }).ToList(),
            Links = { new Link("self", "/api/free-busy") },
        };
    }
}
