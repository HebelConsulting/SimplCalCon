using System.Text;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Objects.Exceptions;
using Calendar = SimplCalCon.Domain.Collections.Calendar;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>A calendar object resource: GET/PUT/DELETE with ETag conditionals, and PROPFIND (ACL-enforced, ADR 0007).</summary>
public sealed class CalDavObjectController(
    IDavRepository repository, IObjectStore objectStore, ISchedulingService scheduling, IAclService acl) : DavControllerBase
{
    [HttpGet("~/dav/calendars/{userId:guid}/{cal}/{name}")]
    public async Task<IActionResult> Get(Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        var (calendar, access) = await ResolveAsync(userId, cal, AclRight.Read, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var calendarObject = await repository.GetCalendarObjectAsync(calendar!.Id, name, cancellationToken);
        if (calendarObject is null)
        {
            return NotFound();
        }

        Response.Headers.ETag = ETag.Format(calendarObject.ConcurrencyToken);
        return Content(calendarObject.Blob, "text/calendar; charset=utf-8");
    }

    [HttpPropfind("~/dav/calendars/{userId:guid}/{cal}/{name}")]
    public async Task<IActionResult> Propfind(Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        var (calendar, access) = await ResolveAsync(userId, cal, AclRight.Read, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var calendarObject = await repository.GetCalendarObjectAsync(calendar!.Id, name, cancellationToken);
        if (calendarObject is null)
        {
            return NotFound();
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var resource = CalDavResources.CalendarObjectResource(CalendarObjectHref(userId, cal, name), calendarObject);
        return DavXml.MultiStatus(MultiStatus.Build(request, [resource]));
    }

    [HttpPut("~/dav/calendars/{userId:guid}/{cal}/{name}")]
    public async Task<IActionResult> Put(Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        var (calendar, access) = await ResolveAsync(userId, cal, AclRight.WriteContent, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var existing = await repository.GetCalendarObjectAsync(calendar!.Id, name, cancellationToken);
        if (PreconditionFailed(existing?.ConcurrencyToken, existing is not null))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var blob = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var result = await objectStore.PutAsync(
                new PutObjectRequest(calendar.Id, name, blob, CurrentUserId), cancellationToken);

            // RFC 6638 automatic scheduling (ADR 0031): deliver REQUEST/REPLY/CANCEL as needed.
            await scheduling.ProcessWriteAsync(calendar.Id, existing?.Blob, blob, CurrentUserId, cancellationToken);

            Response.Headers.ETag = ETag.Format(result.ETag);
            return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent);
        }
        catch (UidConflictException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }
        catch (ObjectStoreException)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
    }

    [HttpDelete("~/dav/calendars/{userId:guid}/{cal}/{name}")]
    public async Task<IActionResult> Delete(Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        var (calendar, access) = await ResolveAsync(userId, cal, AclRight.WriteContent, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var existing = await repository.GetCalendarObjectAsync(calendar!.Id, name, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (PreconditionFailed(existing.ConcurrencyToken, true))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        await objectStore.DeleteAsync(calendar.Id, name, CurrentUserId, cancellationToken);
        await scheduling.ProcessDeleteAsync(calendar.Id, existing.Blob, CurrentUserId, cancellationToken);
        return NoContent();
    }

    private async Task<(Calendar? Collection, IActionResult? Result)> ResolveAsync(
        Guid ownerId, string cal, AclRight required, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(ownerId, cal, cancellationToken);
        if (calendar is null)
        {
            return (null, NotFound());
        }

        return await HasAccessAsync(calendar, required, acl, cancellationToken)
            ? (calendar, null)
            : (null, ForbidDav());
    }

    private bool PreconditionFailed(Guid? currentToken, bool exists)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch == "*" && exists)
        {
            return true;
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(ifMatch))
        {
            return false;
        }

        return currentToken is null
            || !ETag.TryParse(ifMatch, out var token)
            || token != currentToken;
    }
}
