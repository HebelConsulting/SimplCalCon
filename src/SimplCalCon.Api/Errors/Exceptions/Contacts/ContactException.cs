namespace SimplCalCon.Api.Errors.Exceptions.Contacts;

/// <summary>Area base for contact (vCard) operation errors.</summary>
public abstract class ContactException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
