using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplCalCon.Infrastructure.Push;

namespace SimplCalCon.UnitTests;

/// <summary>Guards the demo-only push-endpoint TLS opt-out (ADR 0081): off unless explicitly enabled.</summary>
public sealed class WebPushConfigurationTests
{
    private static WebPushConfiguration Build(WebPushOptions options) =>
        new(Options.Create(options), NullLogger<WebPushConfiguration>.Instance);

    [Fact]
    public void Untrusted_push_tls_is_off_by_default()
    {
        Assert.False(Build(new WebPushOptions()).AllowUntrustedPushEndpointTls);
    }

    [Fact]
    public void Untrusted_push_tls_reflects_the_configured_flag()
    {
        Assert.True(Build(new WebPushOptions { AllowUntrustedPushEndpointTls = true }).AllowUntrustedPushEndpointTls);
    }
}
