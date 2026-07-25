using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Domain.Push;

/// <summary>
/// A client's Web Push subscription to a collection for WebDAV-Push notifications (ADR 0052,
/// RFC 8030). Identified for a resource by its <see cref="Endpoint"/> (the push resource);
/// re-registration with the same endpoint updates the row. Dies with the collection.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    public Guid CollectionId { get; set; }

    public Collection Collection { get; set; } = null!;

    /// <summary>The Web Push endpoint URL the push service delivers to (the "push resource").</summary>
    public required string Endpoint { get; set; }

    /// <summary>The user agent's public key (base64url, uncompressed P-256), for RFC 8291 encryption.</summary>
    public required string P256dh { get; set; }

    /// <summary>The subscription auth secret (base64url).</summary>
    public required string Auth { get; set; }

    /// <summary>Server-side expiry (UTC); the client refreshes before this.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
