using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimplCalCon.Api.Dav;

namespace SimplCalCon.IntegrationTests;

/// <summary>
/// Guards the verbose DAV wire trace (ADR 0033): when enabled it must log the exchange +
/// raise the one-time Warning <b>without corrupting the response</b> (it swaps the response
/// stream), and when disabled it must be a zero-overhead pass-through.
/// </summary>
public sealed class DavWireTraceMiddlewareTests
{
    private const string ResponseXml = "<multistatus xmlns=\"DAV:\"><response/></multistatus>";

    [Fact]
    public async Task Enabled_traces_warns_once_and_preserves_the_response()
    {
        var captured = new List<(LogLevel Level, string Message)>();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new ListLoggerProvider(captured));
        });

        var middleware = new DavWireTraceMiddleware(WriteDavResponse, loggerFactory);
        var context = DavContext("PROPFIND", "/dav/addressbooks/x/", "<propfind/>");
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        // Run twice: the Warning must fire exactly once across requests.
        await middleware.InvokeAsync(context);
        await middleware.InvokeAsync(DavContext("PROPFIND", "/dav/addressbooks/x/", "<propfind/>"));

        Assert.Equal(207, context.Response.StatusCode);
        Assert.Equal(ResponseXml, Encoding.UTF8.GetString(responseStream.ToArray()));
        Assert.Contains(captured, e => e.Level == LogLevel.Trace && e.Message.Contains("PROPFIND"));
        Assert.Equal(1, captured.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Disabled_is_a_passthrough_and_logs_nothing()
    {
        var captured = new List<(LogLevel Level, string Message)>();
        // NullLoggerFactory → IsEnabled(Trace) is false → no buffering, no logs.
        var middleware = new DavWireTraceMiddleware(WriteDavResponse, NullLoggerFactory.Instance);
        var context = DavContext("PROPFIND", "/dav/addressbooks/x/", "<propfind/>");
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        await middleware.InvokeAsync(context);

        Assert.Equal(207, context.Response.StatusCode);
        Assert.Equal(ResponseXml, Encoding.UTF8.GetString(responseStream.ToArray()));
        Assert.Empty(captured);
    }

    [Fact]
    public async Task Non_dav_requests_are_ignored_even_when_enabled()
    {
        var captured = new List<(LogLevel Level, string Message)>();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new ListLoggerProvider(captured));
        });

        var middleware = new DavWireTraceMiddleware(WriteDavResponse, loggerFactory);
        await middleware.InvokeAsync(DavContext("GET", "/api/me", body: null));

        Assert.Empty(captured);
    }

    private static async Task WriteDavResponse(HttpContext context)
    {
        context.Response.StatusCode = 207;
        await context.Response.WriteAsync(ResponseXml);
    }

    private static DefaultHttpContext DavContext(string method, string path, string? body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }

        return context;
    }

    private sealed class ListLoggerProvider(List<(LogLevel, string)> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(sink);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<(LogLevel, string)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                {
                    sink.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
