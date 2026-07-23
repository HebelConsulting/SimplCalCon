using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Calendars;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Api.Controllers;

/// <summary>Events in a calendar. Reads need `read`; writes need `write-content` (ADR 0007, 0009).</summary>
[Route("api/calendars/{calendarId:guid}/events")]
public sealed class EventsController(
    IDavRepository repository, IObjectStore objectStore, IObjectComposer composer, IEventSplitter splitter, IAclService acl)
    : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<EventResource>>> List(
        Guid calendarId, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.Read, cancellationToken);

        var events = fromUtc is not null || toUtc is not null
            ? await repository.QueryCalendarObjectsAsync(calendarId, fromUtc, toUtc, cancellationToken)
            : await repository.ListCalendarObjectsAsync(calendarId, cancellationToken);

        return new CollectionResource<EventResource>
        {
            Items = events.Select(ResourceMapper.MapEvent).ToList(),
            Links = { new Link("self", $"/api/calendars/{calendarId}/events") },
        };
    }

    [HttpGet("{id:guid}", Name = "GetEvent")]
    public async Task<ActionResult<EventResource>> Get(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.Read, cancellationToken);
        var calendarObject = await FindAsync(calendarId, id, cancellationToken);
        return ResourceMapper.MapEvent(calendarObject);
    }

    [HttpPost]
    public async Task<ActionResult<EventResource>> Create(
        Guid calendarId, [FromBody] EventWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var result = await composer.PutEventAsync(calendarId, null, ToInput(request), CurrentUserId, cancellationToken);
        var created = await repository.GetCalendarObjectByIdAsync(result.Id, cancellationToken);
        return CreatedAtRoute("GetEvent", new { calendarId, id = result.Id }, ResourceMapper.MapEvent(created!));
    }

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    public async Task<ActionResult<EventResource>> Update(
        Guid calendarId, Guid id, [FromBody] EventWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        await composer.PutEventAsync(calendarId, existing.ResourceName, ToInput(request), CurrentUserId, cancellationToken);
        var updated = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        return ResourceMapper.MapEvent(updated!);
    }

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Delete(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        await objectStore.DeleteAsync(calendarId, existing.ResourceName, CurrentUserId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Splits an event at a point in time into two same-kind events in this calendar: the
    /// original truncated to end at the split point, and a new copy covering the remainder
    /// (ADR 0027). A genuine state transition, so a verb sub-resource is used (ADR 0009).
    /// </summary>
    [HttpPost("{id:guid}/split")]
    [RequireIfMatch]
    public async Task<ActionResult<SplitEventResource>> Split(
        Guid calendarId, Guid id, [FromBody] SplitEventRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        var atUtc = DateTime.SpecifyKind(request.AtUtc, DateTimeKind.Utc);
        EnsureSplittable(existing, atUtc);

        var result = await splitter.SplitEventAsync(calendarId, existing.ResourceName, atUtc, CurrentUserId, cancellationToken);
        var original = await repository.GetCalendarObjectByIdAsync(result.Original.Id, cancellationToken);
        var created = await repository.GetCalendarObjectByIdAsync(result.Copy.Id, cancellationToken);

        return new SplitEventResource
        {
            Original = ResourceMapper.MapEvent(original!),
            Created = ResourceMapper.MapEvent(created!),
            Links =
            {
                new Link("self", $"/api/calendars/{calendarId}/events/{id}"),
                new Link("created", $"/api/calendars/{calendarId}/events/{result.Copy.Id}"),
            },
        };
    }

    // --- Trash & version history (ADR 0028). Trash/restore act on already-deleted items, so they are If-Match-exempt. ---

    [HttpGet("trash")]
    public async Task<ActionResult<CollectionResource<EventResource>>> ListTrash(Guid calendarId, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.Read, cancellationToken);
        var trashed = await repository.ListTrashedCalendarObjectsAsync(calendarId, cancellationToken);
        return new CollectionResource<EventResource>
        {
            Items = trashed.Select(ResourceMapper.MapEvent).ToList(),
            Links = { new Link("self", $"/api/calendars/{calendarId}/events/trash") },
        };
    }

    [HttpDelete("trash")]
    public async Task<IActionResult> EmptyTrash(Guid calendarId, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        await objectStore.PurgeTrashAsync(calendarId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("trash/{id:guid}")]
    public async Task<IActionResult> Purge(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var trashed = await ResolveTrashedAsync(calendarId, id, cancellationToken);
        await objectStore.PurgeAsync(calendarId, trashed.ResourceName, cancellationToken);
        return NoContent();
    }

    [HttpPost("trash/{id:guid}/restore")]
    public async Task<ActionResult<EventResource>> Restore(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var trashed = await ResolveTrashedAsync(calendarId, id, cancellationToken);
        var result = await objectStore.RestoreAsync(calendarId, trashed.ResourceName, null, CurrentUserId, cancellationToken);
        var restored = await repository.GetCalendarObjectByIdAsync(result!.Id, cancellationToken);
        return ResourceMapper.MapEvent(restored!);
    }

    [HttpGet("{id:guid}/revisions")]
    public async Task<ActionResult<CollectionResource<RevisionResource>>> Revisions(
        Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.Read, cancellationToken);
        var calendarObject = await ResolveAnyAsync(calendarId, id, cancellationToken);
        var revisions = await repository.ListObjectRevisionsAsync(calendarObject.Id, cancellationToken);
        var selfBase = $"/api/calendars/{calendarId}/events/{id}";
        return new CollectionResource<RevisionResource>
        {
            Items = revisions.Select(r => ResourceMapper.MapRevision(r, selfBase)).ToList(),
            Links = { new Link("self", $"{selfBase}/revisions") },
        };
    }

    [HttpPost("{id:guid}/revisions/{number:long}/restore")]
    public async Task<ActionResult<EventResource>> RestoreRevision(
        Guid calendarId, Guid id, long number, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var calendarObject = await ResolveAnyAsync(calendarId, id, cancellationToken);
        var result = await objectStore.RestoreAsync(calendarId, calendarObject.ResourceName, number, CurrentUserId, cancellationToken);
        var restored = await repository.GetCalendarObjectByIdAsync(result!.Id, cancellationToken);
        return ResourceMapper.MapEvent(restored!);
    }

    private async Task<CalendarObject> ResolveTrashedAsync(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        var found = await repository.FindCalendarObjectByIdAsync(id, cancellationToken);
        return found is { IsDeleted: true } && found.CollectionId == calendarId
            ? found
            : throw new ResourceNotFoundException("Trashed event", id);
    }

    private async Task<CalendarObject> ResolveAnyAsync(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        var found = await repository.FindCalendarObjectByIdAsync(id, cancellationToken);
        return found is not null && found.CollectionId == calendarId
            ? found
            : throw new ResourceNotFoundException("Event", id);
    }

    // Splittable = a non-recurring, non-all-day event whose start/end straddle the split point.
    private static void EnsureSplittable(CalendarObject calendarObject, DateTime atUtc)
    {
        if (calendarObject.ComponentType != CalendarComponentType.Event)
        {
            throw new EventNotSplittableException("Only events can be split.");
        }

        if (calendarObject.IsRecurring)
        {
            throw new CannotSplitRecurringException();
        }

        if (calendarObject.IsAllDay)
        {
            throw new EventNotSplittableException("All-day events cannot be split.");
        }

        if (calendarObject.DtStartUtc is not { } start || calendarObject.DtEndUtc is not { } end)
        {
            throw new EventNotSplittableException("The event has no start and end to split.");
        }

        if (atUtc <= start || atUtc >= end)
        {
            throw new SplitPointOutOfRangeException();
        }
    }

    private async Task<Domain.Objects.CalendarObject> FindAsync(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        var calendarObject = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        return calendarObject is not null && calendarObject.CollectionId == calendarId
            ? calendarObject
            : throw new ResourceNotFoundException("Event", id);
    }

    private static EventInput ToInput(EventWriteRequest request) =>
        new(request.Summary, request.StartUtc, request.EndUtc, request.IsAllDay,
            request.Organizer,
            request.Attendees.Select(a => new AttendeeInput(a.Address, a.CommonName)).ToList());
}
