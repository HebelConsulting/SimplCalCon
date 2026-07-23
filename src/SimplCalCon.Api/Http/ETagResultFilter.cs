using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SimplCalCon.Api.Http;

/// <summary>
/// Stamps the <c>ETag</c> response header from a returned resource's concurrency
/// token whenever the action produces a 2xx <see cref="IETaggedResource"/> (ADR 0009).
/// Registered globally so controllers never set ETags by hand.
/// </summary>
public sealed class ETagResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: IETaggedResource resource } result
            && result.StatusCode is null or (>= 200 and < 300))
        {
            context.HttpContext.Response.Headers.ETag = ETag.Format(resource.ConcurrencyToken);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
