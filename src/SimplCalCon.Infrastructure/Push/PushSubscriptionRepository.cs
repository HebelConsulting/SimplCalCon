using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Push;
using SimplCalCon.Infrastructure.Persistence;
using DomainPushSubscription = SimplCalCon.Domain.Push.PushSubscription;

namespace SimplCalCon.Infrastructure.Push;

/// <summary>Stores WebDAV-Push subscriptions per collection (ADR 0052); upsert keyed by (collection, endpoint).</summary>
internal sealed class PushSubscriptionRepository(SimplCalConDbContext dbContext, IClock clock) : IPushSubscriptions
{
    public async Task<PushSubscriptionInfo> RegisterAsync(
        Guid collectionId, string endpoint, string p256dh, string auth, DateTime? expiresAt, CancellationToken cancellationToken)
    {
        var existing = await dbContext.PushSubscriptions
            .FirstOrDefaultAsync(s => s.CollectionId == collectionId && s.Endpoint == endpoint, cancellationToken);

        if (existing is null)
        {
            existing = new DomainPushSubscription
            {
                Id = Guid.NewGuid(),
                CollectionId = collectionId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                ExpiresAt = expiresAt,
                CreatedAt = clock.UtcNow.UtcDateTime,
            };
            dbContext.PushSubscriptions.Add(existing);
        }
        else
        {
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.ExpiresAt = expiresAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new PushSubscriptionInfo(existing.Id, existing.CollectionId, existing.Endpoint, existing.P256dh, existing.Auth, existing.ExpiresAt);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.PushSubscriptions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<PushSubscriptionInfo>> ListForCollectionAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.PushSubscriptions
            .Where(s => s.CollectionId == collectionId)
            .Select(s => new PushSubscriptionInfo(s.Id, s.CollectionId, s.Endpoint, s.P256dh, s.Auth, s.ExpiresAt))
            .ToListAsync(cancellationToken);
}
