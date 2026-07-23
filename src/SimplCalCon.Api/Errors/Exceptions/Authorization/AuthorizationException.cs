namespace SimplCalCon.Api.Errors.Exceptions.Authorization;

/// <summary>Area base for authorization failures.</summary>
public abstract class AuthorizationException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
