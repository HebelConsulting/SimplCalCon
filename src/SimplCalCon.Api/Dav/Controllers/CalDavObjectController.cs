using System.Text;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>A calendar object resource: GET/PUT/DELETE with ETag conditionals, and PROPFIND.</summary>
public sealed class CalDavObjectController(IDavRepository repository, IObjectStore objectStore) : DavControllerBase
{
    [HttpGet("~/dav/calendars/{userId:guid}/{cal}/{name}")]
    public async Task<IActionResult> Get(Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var calendarObject = await FindObjectAsync(userId, cal, name, cancellationToken);
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
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var calendarObject = await FindObjectAsync(userId, cal, name, cancellationToken);
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
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        var existing = await repository.GetCalendarObjectAsync(calendar.Id, name, cancellationToken);
        if (PreconditionFailed(existing))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var blob = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var result = await objectStore.PutAsync(
                new PutObjectRequest(calendar.Id, name, blob, CurrentUserId), cancellationToken);
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
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        var existing = await repository.GetCalendarObjectAsync(calendar.Id, name, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (PreconditionFailed(existing))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        await objectStore.DeleteAsync(calendar.Id, name, CurrentUserId, cancellationToken);
        return NoContent();
    }

    private async Task<CalendarObject?> FindObjectAsync(
        Guid userId, string cal, string name, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        return calendar is null ? null : await repository.GetCalendarObjectAsync(calendar.Id, name, cancellationToken);
    }

    private bool PreconditionFailed(CalendarObject? existing)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch == "*" && existing is not null)
        {
            return true;
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(ifMatch))
        {
            return false;
        }

        return existing is null
            || !ETag.TryParse(ifMatch, out var token)
            || token != existing.ConcurrencyToken;
    }
}
