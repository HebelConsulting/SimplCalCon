using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Calendars;

/// <summary>The occurrence's RECURRENCE-ID path value wasn't a UTC basic-format instant (ADR 0051).</summary>
public sealed class InvalidRecurrenceIdException(string value)
    : CalendarException(
        "INVALID_RECURRENCE_ID",
        StatusCodes.Status400BadRequest,
        $"'{value}' is not a valid RECURRENCE-ID (expected yyyyMMddTHHmmssZ).");
