namespace SimplCalCon.Api.Dav;

/// <summary>
/// Verbose wire-level trace of the DAV surface (ADR 0033 — Trace level): logs each DAV
/// request's method, path, depth and status plus the raw request and response bodies.
/// Deliberately the most verbose signal we emit ("may clutter"), so it is <b>off by
/// default</b> and gated on <c>IsEnabled(LogLevel.Trace)</c> for the
/// <c>SimplCalCon.Dav.Wire</c> category — when Trace is not enabled the middleware is a
/// pass-through with no body buffering. Enable per deployment via configuration/env, e.g.
/// <c>Serilog__MinimumLevel__Override__SimplCalCon.Dav.Wire=Verbose</c>, to diagnose a
/// native client (CalDAV/CardDAV) without attaching a proxy.
/// </summary>
public sealed class DavWireTraceMiddleware
{
    private const string Category = "SimplCalCon.Dav.Wire";

    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private int _warned;

    public DavWireTraceMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger(Category);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_logger.IsEnabled(LogLevel.Trace) || !IsDav(context.Request))
        {
            await _next(context);
            return;
        }

        WarnOnceThatTracingIsActive();

        context.Request.EnableBuffering();
        var requestBody = await ReadRequestBodyAsync(context.Request);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer).ReadToEndAsync();
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            _logger.LogTrace(
                "DAV {Method} {Path}{Query} depth={Depth} ua={UserAgent} -> {StatusCode}\n"
                + "request:\n{RequestBody}\nresponse:\n{ResponseBody}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.Request.Headers.TryGetValue("Depth", out var depth) ? depth.ToString() : "0",
                context.Request.Headers.UserAgent.ToString(),
                context.Response.StatusCode,
                requestBody,
                responseBody);
        }
    }

    // First time a verbose entry is actually written, raise one Warning: leaving this on
    // clutters the log and captures contact/calendar payloads — an admin should act (ADR 0033).
    private void WarnOnceThatTracingIsActive()
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _logger.LogWarning(
                "DAV wire tracing ({Category}) is enabled at Trace: request/response bodies — "
                + "including contact and calendar contents — are being logged. This is verbose and "
                + "unsafe for production; disable it when finished.", Category);
        }
    }

    // The DAV surface plus the RFC 6764 root-discovery methods (PROPFIND on "/").
    private static bool IsDav(HttpRequest request) =>
        request.Path.StartsWithSegments("/dav")
        || request.Method is "PROPFIND" or "REPORT" or "PROPPATCH" or "MKCOL" or "MKCALENDAR";

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
        {
            return "";
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return body;
    }
}
