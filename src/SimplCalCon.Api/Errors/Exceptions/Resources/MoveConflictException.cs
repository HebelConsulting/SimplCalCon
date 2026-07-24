using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Resources;

/// <summary>The target collection already holds an object with the moved item's UID (ADR 0042).</summary>
public sealed class MoveConflictException()
    : ResourceException("MOVE_CONFLICT", StatusCodes.Status409Conflict,
        "The target already contains an entry with the same identifier.");
