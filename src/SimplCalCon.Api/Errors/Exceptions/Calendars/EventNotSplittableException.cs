using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Calendars;

/// <summary>The target object cannot be split (not an event, all-day, or has no time window) (ADR 0027).</summary>
public sealed class EventNotSplittableException(string reason)
    : CalendarException("EVENT_NOT_SPLITTABLE", StatusCodes.Status400BadRequest, reason);
