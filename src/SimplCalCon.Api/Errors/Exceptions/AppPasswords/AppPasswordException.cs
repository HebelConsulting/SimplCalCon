namespace SimplCalCon.Api.Errors.Exceptions.AppPasswords;

/// <summary>Area base for app-password errors.</summary>
public abstract class AppPasswordException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
