using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SimplCalCon.Api.Errors.Exceptions.Concurrency;

namespace SimplCalCon.Api.Errors;

/// <summary>
/// Translates exceptions into RFC 7807 <c>application/problem+json</c> responses
/// (ADR 0009). Any <see cref="ApiException"/> carries its own status + stable
/// <c>errorCode</c>; a stale optimistic-concurrency update surfaces as 412; anything
/// else is a generic 500. Writing through <see cref="IProblemDetailsService"/>
/// guarantees the correct <c>application/problem+json</c> media type (ADR 0012).
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, errorCode, detail) = exception switch
        {
            ApiException api => (api.StatusCode, api.ErrorCode, api.Message),
            DbUpdateConcurrencyException => (
                StatusCodes.Status412PreconditionFailed,
                new EtagMismatchException().ErrorCode,
                new EtagMismatchException().Message),
            SimplCalCon.Domain.Acl.Exceptions.CrossTenantGrantException => (
                StatusCodes.Status400BadRequest, "CROSS_TENANT_SHARE", exception.Message),
            SimplCalCon.Domain.Objects.Exceptions.RevisionNotFoundException => (
                StatusCodes.Status404NotFound, "REVISION_NOT_FOUND", exception.Message),
            SimplCalCon.Domain.Objects.Exceptions.InvalidTakeoutException => (
                StatusCodes.Status400BadRequest, "INVALID_TAKEOUT", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred."),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception translated to {Status}.", status);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Extensions = { ["errorCode"] = errorCode },
        };

        // Surface the underlying exception for unexpected (500) errors during
        // development only; production responses stay generic.
        if (status >= StatusCodes.Status500InternalServerError && environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.ToString();
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
