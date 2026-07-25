using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Push;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>
/// WebDAV-Push registration (ADR 0052): clients POST a <c>push-register</c> document to a
/// collection to subscribe a Web Push endpoint, and DELETE the returned registration URL to
/// unsubscribe. Requires read access to the collection and that WebDAV-Push is enabled (VAPID
/// configured). Delivery happens via <c>WebPushChangeNotifier</c> on the shared change signal.
/// </summary>
public sealed class WebDavPushController(
    IDavRepository repository, IAclService acl, IPushSubscriptions subscriptions,
    IWebPushConfiguration webPush, IClock clock) : DavControllerBase
{
    private const int DefaultTtlDays = 30;

    [HttpPost("~/dav/calendars/{userId:guid}/{cal}")]
    public async Task<IActionResult> RegisterCalendar(Guid userId, string cal, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarAsync(userId, cal, cancellationToken);
        return await RegisterAsync(calendar, cancellationToken);
    }

    [HttpPost("~/dav/addressbooks/{userId:guid}/{book}")]
    public async Task<IActionResult> RegisterAddressBook(Guid userId, string book, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookAsync(userId, book, cancellationToken);
        return await RegisterAsync(addressBook, cancellationToken);
    }

    [HttpDelete("~/dav/push-subscriptions/{id:guid}")]
    public async Task<IActionResult> Unregister(Guid id, CancellationToken cancellationToken)
    {
        await subscriptions.DeleteAsync(id, cancellationToken);
        return NoContent(); // idempotent
    }

    private async Task<IActionResult> RegisterAsync(Collection? collection, CancellationToken cancellationToken)
    {
        if (!webPush.IsEnabled || collection is null)
        {
            return PushNotAvailable();
        }

        if (!await HasAccessAsync(collection, AclRight.Read, acl, cancellationToken))
        {
            return PushNotAvailable();
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        if (body is null || body.Name != DavNames.Push + "push-register")
        {
            return BadRequest();
        }

        var subscription = body.Element(DavNames.Push + "subscription")?.Element(DavNames.Push + "web-push-subscription");
        var endpoint = subscription?.Element(DavNames.Push + "push-resource")?.Value?.Trim();
        var p256dh = subscription?.Element(DavNames.Push + "subscription-public-key")?.Value?.Trim();
        var auth = subscription?.Element(DavNames.Push + "auth-secret")?.Value?.Trim();

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(p256dh) || string.IsNullOrEmpty(auth))
        {
            return BadRequest();
        }

        var expiresAt = ResolveExpiry(body.Element(DavNames.Push + "expires")?.Value);
        var stored = await subscriptions.RegisterAsync(
            collection.Id, endpoint, p256dh, auth, expiresAt, cancellationToken);

        // 204 + absolute registration URL (for unregister) + the actual expiration (IMF-fixdate).
        Response.Headers.Location = $"{Request.Scheme}://{Request.Host}/dav/push-subscriptions/{stored.Id}";
        Response.Headers.Expires = expiresAt.ToString("R", CultureInfo.InvariantCulture);
        return NoContent();
    }

    // Server-decided expiry: cap the client's request at the server TTL.
    private DateTime ResolveExpiry(string? requested)
    {
        var now = clock.UtcNow.UtcDateTime;
        var cap = now.AddDays(DefaultTtlDays);
        return DateTimeOffset.TryParse(requested, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wanted)
               && wanted.UtcDateTime < cap
            ? wanted.UtcDateTime
            : cap;
    }

    private IActionResult PushNotAvailable() => new ContentResult
    {
        StatusCode = StatusCodes.Status403Forbidden,
        ContentType = "application/xml; charset=utf-8",
        Content = new XDocument(
            new XElement(
                DavNames.Dav + "error",
                new XAttribute(XNamespace.Xmlns + "P", DavNames.Push.NamespaceName),
                new XElement(DavNames.Push + "push-not-available"))).ToString(),
    };
}
