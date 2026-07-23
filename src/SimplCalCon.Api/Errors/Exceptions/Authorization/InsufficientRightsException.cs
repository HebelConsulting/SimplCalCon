using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Authorization;

/// <summary>The caller lacks the required rights on the collection (ADR 0007).</summary>
public sealed class InsufficientRightsException()
    : AuthorizationException(
        "INSUFFICIENT_RIGHTS",
        StatusCodes.Status403Forbidden,
        "You do not have the required rights on this collection.");
