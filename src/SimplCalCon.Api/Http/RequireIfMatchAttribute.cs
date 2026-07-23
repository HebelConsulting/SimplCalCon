using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using SimplCalCon.Api.Errors.Exceptions.Concurrency;

namespace SimplCalCon.Api.Http;

/// <summary>
/// Enforces the ETag/If-Match precondition on a mutation (ADR 0009): a missing header
/// is 428, a malformed one is 412. On success it stashes the parsed token (null for
/// <c>*</c>) under <see cref="ItemsKey"/> for the action to pass to the write path.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : Attribute, IActionFilter
{
    public const string ItemsKey = "SimplCalCon.IfMatchToken";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var header = context.HttpContext.Request.Headers.IfMatch.ToString();

        if (string.IsNullOrEmpty(header))
        {
            throw new IfMatchRequiredException();
        }

        if (ETag.IsWildcard(header))
        {
            context.HttpContext.Items[ItemsKey] = null;
            return;
        }

        if (!ETag.TryParse(header, out var token))
        {
            throw new EtagMismatchException();
        }

        context.HttpContext.Items[ItemsKey] = token;
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>Reads the token stashed by the filter: null means the wildcard <c>*</c>.</summary>
    public static Guid? ReadToken(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ItemsKey, out var value) ? value as Guid? : null;
}
