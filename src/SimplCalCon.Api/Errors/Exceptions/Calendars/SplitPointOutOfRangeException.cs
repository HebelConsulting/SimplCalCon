using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Calendars;

/// <summary>The requested split point is not strictly inside the event's start/end window (ADR 0027).</summary>
public sealed class SplitPointOutOfRangeException()
    : CalendarException(
        "SPLIT_POINT_OUT_OF_RANGE",
        StatusCodes.Status400BadRequest,
        "The split point must fall strictly between the event's start and end.");
