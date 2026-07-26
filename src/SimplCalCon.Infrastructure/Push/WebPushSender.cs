using System.Net;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions.Push;
using WebPushLib = WebPush;

namespace SimplCalCon.Infrastructure.Push;

/// <summary>
/// Sends encrypted Web Push messages via the WebPush library (RFC 8291 aes128gcm + RFC 8292 VAPID),
/// ADR 0052. A 404/410 from the push service means the endpoint is dead → <see cref="WebPushDelivery.Gone"/>.
/// </summary>
internal sealed class WebPushSender : IWebPushSender
{
    private readonly WebPushConfiguration configuration;
    private readonly ILogger<WebPushSender> logger;
    private readonly WebPushLib.WebPushClient client;

    public WebPushSender(WebPushConfiguration configuration, ILogger<WebPushSender> logger)
    {
        this.configuration = configuration;
        this.logger = logger;

        // Demo/LAN only (ADR 0081): trust a self-hosted ntfy behind Caddy's internal CA. Off in production.
        if (configuration.AllowUntrustedPushEndpointTls)
        {
            client = new WebPushLib.WebPushClient(httpClientHandler: new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }
        else
        {
            client = new WebPushLib.WebPushClient();
        }
    }

    public async Task<WebPushDelivery> SendAsync(
        string endpoint, string p256dh, string auth, string payload, CancellationToken cancellationToken)
    {
        if (!configuration.IsEnabled)
        {
            return WebPushDelivery.Failed;
        }

        try
        {
            var subscription = new WebPushLib.PushSubscription(endpoint, p256dh, auth);
            var vapid = new WebPushLib.VapidDetails(
                configuration.Subject, configuration.VapidPublicKey, configuration.VapidPrivateKey);
            await client.SendNotificationAsync(subscription, payload, vapid);
            return WebPushDelivery.Delivered;
        }
        catch (WebPushLib.WebPushException ex)
        {
            var gone = ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            if (!gone)
            {
                logger.LogWarning(ex, "Web Push send failed ({Status}) for {Endpoint}.", ex.StatusCode, endpoint);
            }

            return gone ? WebPushDelivery.Gone : WebPushDelivery.Failed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Web Push send errored for {Endpoint}.", endpoint);
            return WebPushDelivery.Failed;
        }
    }
}
