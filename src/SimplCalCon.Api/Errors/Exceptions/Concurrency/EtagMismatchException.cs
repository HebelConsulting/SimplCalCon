using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Concurrency;

/// <summary>The supplied <c>If-Match</c> ETag did not match the current resource (412).</summary>
public sealed class EtagMismatchException()
    : ConcurrencyException(
        "ETAG_MISMATCH",
        StatusCodes.Status412PreconditionFailed,
        "The resource was modified by another request; re-fetch it and retry with the current ETag.");
