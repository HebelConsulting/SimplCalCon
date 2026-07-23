using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Concurrency;

/// <summary>A mutation was attempted without the required <c>If-Match</c> header (428).</summary>
public sealed class IfMatchRequiredException()
    : ConcurrencyException(
        "IF_MATCH_REQUIRED",
        StatusCodes.Status428PreconditionRequired,
        "This mutation requires an If-Match header carrying the resource's current ETag.");
