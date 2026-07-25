using System.Xml.Linq;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Push;

namespace SimplCalCon.Api.Dav;

/// <summary>
/// Advertises WebDAV-Push capability on a collection's PROPFIND response (ADR 0052): the
/// <c>web-push</c> transport with the server's VAPID public key, the collection's stable
/// <c>topic</c>, and the supported triggers. No-op when push is disabled (no VAPID key).
/// </summary>
internal static class DavPushAdvertisement
{
    public static void Apply(DavResource resource, Guid collectionId, string? vapidPublicKey)
    {
        if (string.IsNullOrEmpty(vapidPublicKey))
        {
            return;
        }

        resource.Set(
            DavNames.PushTransports,
            new XElement(
                DavNames.PushWebPush,
                new XElement(DavNames.PushVapidPublicKey, new XAttribute("type", "p256ecdsa"), vapidPublicKey)));
        resource.Set(DavNames.PushTopic, PushTopic.For(collectionId));
        resource.Set(
            DavNames.PushSupportedTriggers,
            new XElement(DavNames.PushContentUpdate, new XElement(DavNames.Dav + "depth", "1")));
    }
}
