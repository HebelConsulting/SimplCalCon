namespace SimplCalCon.Api.Errors.Exceptions.Calendars;

/// <summary>Area base for calendar-object operation errors (events/tasks).</summary>
public abstract class CalendarException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
