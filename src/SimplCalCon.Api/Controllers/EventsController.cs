using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Events in a calendar. Reads need `read`; writes need `write-content` (ADR 0007, 0009).</summary>
[Route("api/calendars/{calendarId:guid}/events")]
public sealed class EventsController(
    IDavRepository repository, IObjectStore objectStore, IObjectComposer composer, IAclService acl)
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

    private async Task<Domain.Objects.CalendarObject> FindAsync(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        var calendarObject = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        return calendarObject is not null && calendarObject.CollectionId == calendarId
            ? calendarObject
            : throw new ResourceNotFoundException("Event", id);
    }

    private static EventInput ToInput(EventWriteRequest request) =>
        new(request.Summary, request.StartUtc, request.EndUtc, request.IsAllDay);
}
