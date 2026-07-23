using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Calendars;

/// <summary>A recurring event cannot be split at a point in time (ADR 0027, deferred).</summary>
public sealed class CannotSplitRecurringException()
    : CalendarException(
        "CANNOT_SPLIT_RECURRING",
        StatusCodes.Status400BadRequest,
        "A recurring event cannot be split.");
