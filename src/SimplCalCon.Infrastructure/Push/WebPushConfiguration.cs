using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Push;

namespace SimplCalCon.Infrastructure.Push;

/// <summary>Bound from the <c>SimplCalCon:WebPush</c> config section (ADR 0052).</summary>
public sealed class WebPushOptions
{
    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }

    /// <summary>VAPID contact (mailto: or https:) sent to the push service.</summary>
    public string? Subject { get; set; }

    /// <summary>Development only: generate an ephemeral VAPID key pair when none is configured (subscriptions reset on restart).</summary>
    public bool AllowEphemeralKeys { get; set; }

    /// <summary>Default subscription lifetime when the client doesn't request one.</summary>
    public int SubscriptionTtlDays { get; set; } = 30;

    /// <summary>
    /// DEMO/DEV ONLY (ADR 0081): skip TLS certificate validation when sending push to the endpoint —
    /// needed for a self-hosted ntfy behind Caddy's internal CA on the LAN. Never enable in production.
    /// </summary>
    public bool AllowUntrustedPushEndpointTls { get; set; }
}

/// <summary>
/// Resolves the VAPID key material for WebDAV-Push (ADR 0052): configured keys in production, an
/// ephemeral pair in development (<see cref="WebPushOptions.AllowEphemeralKeys"/>), else disabled.
/// A singleton — the ephemeral pair is stable for the process lifetime.
/// </summary>
internal sealed class WebPushConfiguration : IWebPushConfiguration
{
    public WebPushConfiguration(
        IOptions<WebPushOptions> options, IHostEnvironment environment, ILogger<WebPushConfiguration> logger)
    {
        var settings = options.Value;
        Subject = string.IsNullOrWhiteSpace(settings.Subject) ? "mailto:webpush@simplcalcon.example" : settings.Subject;
        SubscriptionTtlDays = settings.SubscriptionTtlDays > 0 ? settings.SubscriptionTtlDays : 30;

        // Defense in depth (ADR 0081): honour the TLS-skip flag ONLY in Development, even if configured
        // true — so it can never disable push-endpoint validation in a production deployment.
        AllowUntrustedPushEndpointTls = settings.AllowUntrustedPushEndpointTls && environment.IsDevelopment();
        if (settings.AllowUntrustedPushEndpointTls && !environment.IsDevelopment())
        {
            logger.LogWarning(
                "WebDAV-Push: SimplCalCon:WebPush:AllowUntrustedPushEndpointTls is set but IGNORED outside Development " +
                "(environment is {Environment}) — push-endpoint TLS validation stays ON.", environment.EnvironmentName);
        }
        else if (AllowUntrustedPushEndpointTls)
        {
            logger.LogWarning(
                "WebDAV-Push: push-endpoint TLS validation is DISABLED (SimplCalCon:WebPush:AllowUntrustedPushEndpointTls, " +
                "Development only) — demo/LAN only (self-hosted ntfy behind an internal CA).");
        }

        if (!string.IsNullOrWhiteSpace(settings.VapidPublicKey) && !string.IsNullOrWhiteSpace(settings.VapidPrivateKey))
        {
            VapidPublicKey = settings.VapidPublicKey;
            VapidPrivateKey = settings.VapidPrivateKey;
            IsEnabled = true;
        }
        else if (settings.AllowEphemeralKeys)
        {
            var keys = WebPush.VapidHelper.GenerateVapidKeys();
            VapidPublicKey = keys.PublicKey;
            VapidPrivateKey = keys.PrivateKey;
            IsEnabled = true;
            logger.LogWarning(
                "WebDAV-Push: using EPHEMERAL VAPID keys — push subscriptions reset on restart. " +
                "Configure SimplCalCon:WebPush:VapidPublicKey/VapidPrivateKey for production.");
        }
        else
        {
            IsEnabled = false;
            logger.LogInformation("WebDAV-Push disabled: no VAPID key pair configured.");
        }
    }

    public bool IsEnabled { get; }

    public string? VapidPublicKey { get; }

    public string? VapidPrivateKey { get; }

    public string Subject { get; }

    public int SubscriptionTtlDays { get; }

    public bool AllowUntrustedPushEndpointTls { get; }
}
