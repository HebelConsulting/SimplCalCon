using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Resources;

/// <summary>The requested REST resource does not exist (or the caller can't see it).</summary>
public sealed class ResourceNotFoundException(string resource, Guid id)
    : ResourceException("NOT_FOUND", StatusCodes.Status404NotFound, $"{resource} '{id}' was not found.");
