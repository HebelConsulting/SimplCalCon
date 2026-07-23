namespace SimplCalCon.Api.Errors;

/// <summary>
/// Base type for every API error that maps to a specific RFC 7807 response. Abstract
/// on purpose: concrete, intent-named subclasses encapsulate the code/status/message
/// once at the type, so throw sites read intent-first and no bare
/// <c>new ApiException("CODE", ...)</c> is possible (see CLAUDE.md). Exceptions form a
/// two-level hierarchy: a per-area base inherits from this, and each concrete error
/// inherits from that area base.
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(string errorCode, int statusCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    /// <summary>Stable machine-readable code surfaced as the Problem Details <c>errorCode</c>.</summary>
    public string ErrorCode { get; }

    public int StatusCode { get; }
}
