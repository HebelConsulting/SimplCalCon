namespace SimplCalCon.Api.Errors.Exceptions.Resources;

/// <summary>Area base for REST resource errors.</summary>
public abstract class ResourceException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
