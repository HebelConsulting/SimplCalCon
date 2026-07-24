namespace SimplCalCon.Api.Errors.Exceptions.Users;

/// <summary>Area base for user/profile operation errors.</summary>
public abstract class UserException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
