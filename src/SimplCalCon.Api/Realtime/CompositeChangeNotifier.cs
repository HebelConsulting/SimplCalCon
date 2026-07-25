using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Api.Realtime;

/// <summary>
/// Fans the change signal out to every transport (ADR 0052): SignalR for the web client and
/// WebDAV-Push for native DAV clients. Each transport is isolated — one failing must not stop the
/// others (and, per ADR 0049, must never fail the write; the caller in <c>ObjectStore</c> also guards).
/// </summary>
internal sealed class CompositeChangeNotifier(
    IEnumerable<IChangeNotifier> notifiers, ILogger<CompositeChangeNotifier> logger) : IChangeNotifier
{
    public Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken) =>
        FanOutAsync(n => n.CollectionChangedAsync(collectionId, cancellationToken));

    public Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken) =>
        FanOutAsync(n => n.InvitationsChangedAsync(userId, cancellationToken));

    public Task SharesChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        FanOutAsync(n => n.SharesChangedAsync(userIds, cancellationToken));

    public Task AdminChangedAsync(Guid tenantId, CancellationToken cancellationToken) =>
        FanOutAsync(n => n.AdminChangedAsync(tenantId, cancellationToken));

    private async Task FanOutAsync(Func<IChangeNotifier, Task> send)
    {
        foreach (var notifier in notifiers)
        {
            try
            {
                await send(notifier);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A change-notification transport ({Transport}) failed.", notifier.GetType().Name);
            }
        }
    }
}
