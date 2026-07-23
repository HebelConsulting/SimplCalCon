namespace SimplCalCon.Api.Errors.Exceptions.Concurrency;

/// <summary>Area base for optimistic-concurrency / precondition errors (ADR 0009).</summary>
public abstract class ConcurrencyException(string errorCode, int statusCode, string message)
    : ApiException(errorCode, statusCode, message);
