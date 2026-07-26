using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Calendars the caller owns or has a grant on (ADR 0009, 0010).</summary>
[Route("api/calendars")]
public sealed class CalendarsController(
    IDavRepository repository, IObjectImportExport importExport, IAclService acl,
    IUserCollectionColorService colors) : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<CalendarResource>>> List(CancellationToken cancellationToken)
    {
        var calendars = await repository.ListAccessibleCalendarsAsync(CurrentUserId, cancellationToken);
        var myColors = await colors.GetOverridesAsync(CurrentUserId, calendars.Select(c => c.Id).ToList(), cancellationToken);
        return new CollectionResource<CalendarResource>
        {
            Items = calendars.Select(c => ResourceMapper.MapCalendar(c, CurrentUserId, myColors.GetValueOrDefault(c.Id))).ToList(),
            Links = { new Link("self", "/api/calendars") },
        };
    }

    [HttpHead]
    public IActionResult HeadList() => Ok();

    [HttpGet("{id:guid}", Name = "GetCalendar")]
    public async Task<ActionResult<CalendarResource>> Get(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        var myColor = await colors.GetOverrideAsync(CurrentUserId, id, cancellationToken);
        return ResourceMapper.MapCalendar(calendar, CurrentUserId, myColor);
    }

    // The caller's personal colour override (ADR 0066): any reader may set/clear their own colour.
    [HttpPut("{id:guid}/color")]
    public async Task<IActionResult> SetColor(Guid id, [FromBody] CollectionColorRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        await colors.SetAsync(CurrentUserId, id, request.Color, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/color")]
    public async Task<IActionResult> ClearColor(Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        await colors.ClearAsync(CurrentUserId, id, cancellationToken);
        return NoContent();
    }

    // Read-only subscription feed (ADR 0069): owner enables/resets (fresh token) or disables it.
    [HttpPut("{id:guid}/feed")]
    public async Task<ActionResult<CalendarResource>> EnableFeed(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        if (calendar.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        calendar.FeedToken = await repository.SetFeedTokenAsync(id, enabled: true, cancellationToken);
        return ResourceMapper.MapCalendar(calendar, CurrentUserId);
    }

    [HttpDelete("{id:guid}/feed")]
    public async Task<IActionResult> DisableFeed(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        if (calendar.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        await repository.SetFeedTokenAsync(id, enabled: false, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<CalendarResource>> Create(
        [FromBody] CalendarCreateRequest request, CancellationToken cancellationToken)
    {
        if (CurrentTenantId is not { } tenantId)
        {
            throw new InsufficientRightsException();
        }

        var calendar = await repository.CreateCalendarAsync(
            CurrentUserId, tenantId, ResourceNames.Slug(request.Name), request.Name,
            request.SupportsEvents, request.SupportsTasks, cancellationToken);

        return CreatedAtRoute("GetCalendar", new { id = calendar.Id }, ResourceMapper.MapCalendar(calendar, CurrentUserId));
    }

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    public async Task<ActionResult<CalendarResource>> Update(
        Guid id, [FromBody] CollectionUpdateRequest request, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);

        // Renaming/recolouring a collection is owner-only (ADR 0023, 0041, 0062).
        if (calendar.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        EnsureIfMatch(calendar.ConcurrencyToken);
        var updated = (Calendar)(await repository.UpdateCollectionAsync(id, request.Name, request.Color, cancellationToken))!;
        return ResourceMapper.MapCalendar(updated, CurrentUserId);
    }

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);

        // Deleting a collection is owner-only (ADR 0023).
        if (calendar.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        EnsureIfMatch(calendar.ConcurrencyToken);
        await repository.DeleteCalendarAsync(calendar.OwnerId, calendar.ResourceName, cancellationToken);
        return NoContent();
    }

    // --- Deleted-collection recovery (ADR 0075): owner-only. Restore is If-Match-exempt (acts on an
    //     already-deleted collection, like the object-trash restore in ADR 0028). ---

    [HttpGet("deleted")]
    public async Task<ActionResult<CollectionResource<CalendarResource>>> ListDeleted(CancellationToken cancellationToken)
    {
        var deleted = await repository.ListDeletedCalendarsAsync(CurrentUserId, cancellationToken);
        return new CollectionResource<CalendarResource>
        {
            Items = deleted.Select(c => ResourceMapper.MapCalendar(c, CurrentUserId)).ToList(),
            Links = { new Link("self", "/api/calendars/deleted") },
        };
    }

    [HttpHead("deleted")]
    public IActionResult HeadDeleted() => Ok();

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<CalendarResource>> Restore(Guid id, CancellationToken cancellationToken)
    {
        var restored = await repository.RestoreCalendarAsync(id, CurrentUserId, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        return ResourceMapper.MapCalendar(restored, CurrentUserId);
    }

    // --- Import / export (ADR 0013/0029). A bulk write/read is a genuine action, so a verb sub-resource is used. ---

    [HttpPost("{id:guid}/import")]
    public async Task<ActionResult<ImportResultResource>> Import(
        Guid id, IFormFile? file, [FromForm] string? onConflict, [FromForm] bool? separateCollections,
        [FromForm] bool? mergeByName, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.WriteContent, cancellationToken);
        if (file is null or { Length: 0 })
        {
            return BadRequest("An .ics or .zip file is required.");
        }

        var bytes = await Portability.ReadBytesAsync(file, cancellationToken);
        try
        {
            // A zip + "separate" recreates each file as its own new calendar (ADR 0040).
            if (separateCollections == true && Portability.IsZip(file, bytes))
            {
                if (CurrentTenantId is not { } tenantId)
                {
                    throw new InsufficientRightsException();
                }

                var result = await importExport.ImportArchiveToNewCollectionsAsync(
                    CurrentUserId, tenantId, isCalendar: true, bytes, Portability.Conflict(onConflict),
                    mergeByName != false, cancellationToken);
                return Portability.Map(result);
            }

            var outcome = await Portability.RunImportAsync(importExport, id, file, bytes, onConflict, CurrentUserId, cancellationToken);
            return Portability.Map(outcome);
        }
        catch (System.IO.InvalidDataException)
        {
            return BadRequest("The uploaded file is not a valid zip archive.");
        }
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);

        var document = await importExport.ExportAsync(id, cancellationToken);
        return Portability.Download(document, "text/calendar", $"{calendar.ResourceName}.ics");
    }

    [HttpHead("{id:guid}/export")]
    public async Task<IActionResult> HeadExport(Guid id, CancellationToken cancellationToken)
    {
        _ = await repository.GetCalendarByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        return Ok();
    }
}
