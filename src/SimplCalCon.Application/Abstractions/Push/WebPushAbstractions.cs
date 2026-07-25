namespace SimplCalCon.Application.Abstractions.Push;

/// <summary>WebDAV-Push server configuration (ADR 0052): whether push is available + the VAPID public key to advertise.</summary>
public interface IWebPushConfiguration
{
    /// <summary>True when a VAPID key pair is available (configured, or ephemeral in dev); gates advertisement + registration.</summary>
    bool IsEnabled { get; }

    /// <summary>The VAPID public key (base64url, uncompressed P-256) advertised in PROPFIND; null when disabled.</summary>
    string? VapidPublicKey { get; }
}

/// <summary>Persistence of WebDAV-Push subscriptions (RFC 8030 endpoints) per collection (ADR 0052).</summary>
public interface IPushSubscriptions
{
    /// <summary>Upserts a subscription by (collection, endpoint) — re-registration updates it. Returns the stored row.</summary>
    Task<PushSubscriptionInfo> RegisterAsync(
        Guid collectionId, string endpoint, string p256dh, string auth, DateTime? expiresAt, CancellationToken cancellationToken);

    /// <summary>Removes a subscription by id (unregister / pruning a gone endpoint). Returns false if absent.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscriptionInfo>> ListForCollectionAsync(Guid collectionId, CancellationToken cancellationToken);
}

public sealed record PushSubscriptionInfo(
    Guid Id, Guid CollectionId, string Endpoint, string P256dh, string Auth, DateTime? ExpiresAt);

/// <summary>Sends one encrypted Web Push message (RFC 8291 + VAPID 8292) to a subscription endpoint (ADR 0052).</summary>
public interface IWebPushSender
{
    Task<WebPushDelivery> SendAsync(
        string endpoint, string p256dh, string auth, string payload, CancellationToken cancellationToken);
}

/// <summary>The outcome of a Web Push send: <see cref="Gone"/> means the endpoint is dead (404/410) → prune it.</summary>
public enum WebPushDelivery
{
    Delivered,
    Gone,
    Failed,
}

/// <summary>
/// Derives the stable, opaque WebDAV-Push <c>topic</c> for a collection (ADR 0052): base64url of the
/// collection id. Advertised in PROPFIND and echoed in the push message so the client correlates them.
/// </summary>
public static class PushTopic
{
    public static string For(Guid collectionId) =>
        Convert.ToBase64String(collectionId.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
