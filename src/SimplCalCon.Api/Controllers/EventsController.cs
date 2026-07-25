using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Calendars;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Api.Controllers;

/// <summary>Events in a calendar. Reads need `read`; writes need `write-content` (ADR 0007, 0009).</summary>
[Route("api/calendars/{calendarId:guid}/events")]
public sealed class EventsController(
    IDavRepository repository, IObjectStore objectStore, IObjectComposer composer, IEventSplitter splitter,
    IRecurrenceEditor recurrenceEditor, ISchedulingService scheduling, IAclService acl)
    : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<EventResource>>> List(
        Guid calendarId, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] bool expand,
        CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.Read, cancellationToken);

        // expand=true over a bounded window returns one item per recurrence occurrence (ADR 0050);
        // otherwise recurring events appear once at their master start.
        if (expand && fromUtc is { } from && toUtc is { } to)
        {
            var occurrences = await repository.QueryCalendarOccurrencesAsync(
                calendarId, DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc),
                cancellationToken);
            return new CollectionResource<EventResource>
            {
                Items = occurrences
                    .Select(o => ResourceMapper.MapEvent(o.Object, o.StartUtc, o.EndUtc, o.RecurrenceIdUtc, o.Summary, o.Location))
                    .ToList(),
                Links = { new Link("self", $"/api/calendars/{calendarId}/events") },
            };
        }

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
        // Deliver iTIP invitations for a web-created event with attendees (ADR 0045), mirroring the DAV path.
        await scheduling.ProcessWriteAsync(calendarId, null, created!.Blob, CurrentUserId, cancellationToken);
        return CreatedAtRoute("GetEvent", new { calendarId, id = result.Id }, ResourceMapper.MapEvent(created));
    }

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    public async Task<ActionResult<EventResource>> Update(
        Guid calendarId, Guid id, [FromBody] EventWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        var oldBlob = existing.Blob;
        await composer.PutEventAsync(calendarId, existing.ResourceName, ToInput(request), CurrentUserId, cancellationToken);
        var updated = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        await scheduling.ProcessWriteAsync(calendarId, oldBlob, updated!.Blob, CurrentUserId, cancellationToken);
        return ResourceMapper.MapEvent(updated);
    }

    /// <summary>
    /// Edits a single occurrence of a recurring series (ADR 0051): <c>scope=this</c> writes a
    /// RECURRENCE-ID override; <c>scope=following</c> ends the series here and starts a new one from
    /// the edited fields. ("All events" uses the plain <see cref="Update"/>.) The recurrence-id is
    /// the occurrence's original UTC slot in RFC 5545 basic form (<c>yyyyMMddTHHmmssZ</c>).
    /// </summary>
    [HttpPut("{id:guid}/occurrences/{recurrenceId}")]
    [RequireIfMatch]
    public async Task<ActionResult<EventResource>> UpdateOccurrence(
        Guid calendarId, Guid id, string recurrenceId, [FromQuery] string? scope,
        [FromBody] EventWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);
        var recurrenceIdUtc = ParseRecurrenceId(recurrenceId);
        var oldBlob = existing.Blob;

        if (scope == "following")
        {
            var newSeries = await recurrenceEditor.SplitSeriesAsync(
                calendarId, existing.ResourceName, recurrenceIdUtc, ToInput(request), CurrentUserId, cancellationToken);

            // The old series was shortened (a modification) and a new series was created — invite for both (ADR 0053).
            var truncated = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
            await scheduling.ProcessWriteAsync(calendarId, oldBlob, truncated!.Blob, CurrentUserId, cancellationToken);
            var created = await repository.GetCalendarObjectByIdAsync(newSeries.Id, cancellationToken);
            await scheduling.ProcessWriteAsync(calendarId, null, created!.Blob, CurrentUserId, cancellationToken);
        }
        else
        {
            await recurrenceEditor.OverrideOccurrenceAsync(
                calendarId, existing.ResourceName, recurrenceIdUtc, ToInput(request), CurrentUserId, cancellationToken);

            // A single-occurrence override is a modification of the series — invite attendees (ADR 0053).
            var overridden = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
            await scheduling.ProcessWriteAsync(calendarId, oldBlob, overridden!.Blob, CurrentUserId, cancellationToken);
        }

        var updated = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        return ResourceMapper.MapEvent(updated!);
    }

    /// <summary>Deletes a single occurrence (<c>scope=this</c> → EXDATE) or this and all following (<c>scope=following</c>) — ADR 0051.</summary>
    [HttpDelete("{id:guid}/occurrences/{recurrenceId}")]
    [RequireIfMatch]
    public async Task<IActionResult> DeleteOccurrence(
        Guid calendarId, Guid id, string recurrenceId, [FromQuery] string? scope, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);
        var recurrenceIdUtc = ParseRecurrenceId(recurrenceId);
        var oldBlob = existing.Blob;

        if (scope == "following")
        {
            await recurrenceEditor.TruncateSeriesAsync(
                calendarId, existing.ResourceName, recurrenceIdUtc, CurrentUserId, cancellationToken);
        }
        else
        {
            await recurrenceEditor.ExcludeOccurrenceAsync(
                calendarId, existing.ResourceName, recurrenceIdUtc, CurrentUserId, cancellationToken);
        }

        // Excluding/truncating an occurrence is a modification of the series (the object still exists),
        // so attendees are notified via a REQUEST reflecting the EXDATE/shortened rule — not a full CANCEL (ADR 0053).
        var updated = await repository.GetCalendarObjectByIdAsync(id, cancellationToken);
        if (updated is not null)
        {
            await scheduling.ProcessWriteAsync(calendarId, oldBlob, updated.Blob, CurrentUserId, cancellationToken);
        }

        return NoContent();
    }

    private static DateTime ParseRecurrenceId(string value) =>
        DateTime.TryParseExact(
            value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : throw new InvalidRecurrenceIdException(value);

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Delete(Guid calendarId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        var deletedBlob = existing.Blob;
        await objectStore.DeleteAsync(calendarId, existing.ResourceName, CurrentUserId, cancellationToken);
        // Organizer delete → CANCEL; attendee delete → decline (ADR 0045, 0048), mirroring the DAV path.
        await scheduling.ProcessDeleteAsync(calendarId, deletedBlob, CurrentUserId, cancellationToken);
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

    /// <summary>
    /// Moves the event to another calendar (same tenant; write access to both): written to the
    /// target and removed from this calendar. A genuine state transition (ADR 0009, 0042).
    /// </summary>
    [HttpPost("{id:guid}/move")]
    [RequireIfMatch]
    public async Task<IActionResult> Move(
        Guid calendarId, Guid id, [FromBody] MoveObjectRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(calendarId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(calendarId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        if (request.TargetId == calendarId)
        {
            return NoContent(); // already in this calendar
        }

        var target = await repository.GetCalendarByIdAsync(request.TargetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Calendar", request.TargetId);
        await RequireRightsAsync(target.Id, AclRight.WriteContent, cancellationToken);

        try
        {
            await objectStore.PutAsync(
                new PutObjectRequest(target.Id, existing.ResourceName, existing.Blob, CurrentUserId), cancellationToken);
        }
        catch (UidConflictException)
        {
            throw new MoveConflictException();
        }

        await objectStore.DeleteAsync(calendarId, existing.ResourceName, CurrentUserId, cancellationToken);
        return NoContent();
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
            request.Location,
            request.Organizer,
            request.Attendees.Select(a => new AttendeeInput(a.Address, a.CommonName)).ToList(),
            request.Recurrence,
            request.RecurrenceRule);
}
