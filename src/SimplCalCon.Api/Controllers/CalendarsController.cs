using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Calendars the caller owns or has a grant on (ADR 0009, 0010).</summary>
[Route("api/calendars")]
public sealed class CalendarsController(IDavRepository repository, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<CalendarResource>>> List(CancellationToken cancellationToken)
    {
        var calendars = await repository.ListAccessibleCalendarsAsync(CurrentUserId, cancellationToken);
        return new CollectionResource<CalendarResource>
        {
            Items = calendars.Select(c => ResourceMapper.MapCalendar(c, CurrentUserId)).ToList(),
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
        return ResourceMapper.MapCalendar(calendar, CurrentUserId);
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
}
