using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplCalCon.Infrastructure.Push;

namespace SimplCalCon.UnitTests;

/// <summary>
/// Guards the demo-only push-endpoint TLS opt-out (ADR 0081): off by default, and honoured ONLY in
/// Development even when the flag is set (defense in depth so it can't disable validation in production).
/// </summary>
public sealed class WebPushConfigurationTests
{
    private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static WebPushConfiguration Build(WebPushOptions options, string environment = "Development") =>
        new(Options.Create(options), new FakeEnvironment(environment), NullLogger<WebPushConfiguration>.Instance);

    [Fact]
    public void Untrusted_push_tls_is_off_by_default()
    {
        Assert.False(Build(new WebPushOptions()).AllowUntrustedPushEndpointTls);
    }

    [Fact]
    public void Untrusted_push_tls_is_honoured_when_set_in_Development()
    {
        Assert.True(Build(new WebPushOptions { AllowUntrustedPushEndpointTls = true }, "Development").AllowUntrustedPushEndpointTls);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Untrusted_push_tls_is_ignored_outside_Development_even_when_set(string environment)
    {
        Assert.False(Build(new WebPushOptions { AllowUntrustedPushEndpointTls = true }, environment).AllowUntrustedPushEndpointTls);
    }
}
