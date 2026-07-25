using Microsoft.AspNetCore.SignalR;
using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Api.Realtime;

/// <summary>
/// SignalR-backed <see cref="IChangeNotifier"/> (ADR 0049): broadcasts change signals to the
/// per-collection and per-user groups the <see cref="NotificationHub"/> maintains. Replaces the
/// Infrastructure default no-op notifier; registered as a singleton (the hub context is thread-safe).
/// </summary>
internal sealed class SignalRChangeNotifier(IHubContext<NotificationHub> hub) : IChangeNotifier
{
    public Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken) =>
        hub.Clients.Group(NotificationHub.CollectionGroup(collectionId))
            .SendAsync("CollectionChanged", collectionId, cancellationToken);

    public Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken) =>
        hub.Clients.Group(NotificationHub.UserGroup(userId))
            .SendAsync("InvitationsChanged", cancellationToken);

    public Task SharesChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Groups(userIds.Select(NotificationHub.UserGroup).ToList())
                .SendAsync("SharesChanged", cancellationToken);
}
