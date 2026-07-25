using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Push;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Push;

/// <summary>
/// Delivers WebDAV-Push notifications on the shared post-commit change signal (ADR 0052, on top of
/// ADR 0049's <see cref="IChangeNotifier"/>): on a collection change, encrypts and sends a
/// <c>push-message</c> (topic + sync-token) to every subscription, pruning gone/expired endpoints.
/// A singleton — it opens its own scope for DB access.
/// </summary>
public sealed class WebPushChangeNotifier(
    IServiceScopeFactory scopeFactory,
    IWebPushSender sender,
    IWebPushConfiguration configuration,
    IClock clock,
    ILogger<WebPushChangeNotifier> logger) : IChangeNotifier
{
    private static readonly XNamespace Push = "https://bitfire.at/webdav-push";
    private static readonly XNamespace Dav = "DAV:";

    public async Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        if (!configuration.IsEnabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();

        var subscriptions = await dbContext.PushSubscriptions
            .Where(s => s.CollectionId == collectionId)
            .ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var changeSequence = await dbContext.Collections
            .Where(c => c.Id == collectionId)
            .Select(c => c.ChangeSequence)
            .FirstOrDefaultAsync(cancellationToken);

        var payload = BuildPushMessage(PushTopic.For(collectionId), SyncToken(changeSequence));
        var now = clock.UtcNow.UtcDateTime;
        var pruned = 0;

        foreach (var subscription in subscriptions)
        {
            if (subscription.ExpiresAt is { } expiry && expiry < now)
            {
                dbContext.PushSubscriptions.Remove(subscription);
                pruned++;
                continue;
            }

            var delivery = await sender.SendAsync(
                subscription.Endpoint, subscription.P256dh, subscription.Auth, payload, cancellationToken);
            if (delivery == WebPushDelivery.Gone)
            {
                dbContext.PushSubscriptions.Remove(subscription);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogDebug("WebDAV-Push: pruned {Count} gone/expired subscription(s) for collection {CollectionId}.", pruned, collectionId);
        }
    }

    // WebDAV-Push has no user-scoped signal; the schedule-inbox is itself a collection.
    public Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    // Must match the sync-token the DAV surface returns (DavTokens.Format) so the client can dedupe.
    private static string SyncToken(long changeSequence) => $"https://simplcalcon.example/ns/sync/{changeSequence}";

    private static string BuildPushMessage(string topic, string syncToken)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                Push + "push-message",
                new XAttribute(XNamespace.Xmlns + "D", Dav.NamespaceName),
                new XElement(Push + "topic", topic),
                new XElement(Push + "content-update", new XElement(Dav + "sync-token", syncToken))));
        return document.Declaration + Environment.NewLine + document;
    }
}
