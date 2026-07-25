using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// The default no-transport <see cref="IChangeNotifier"/> (ADR 0049): used by hosts that don't
/// wire SignalR (design-time, unit tests). The Api replaces it with a SignalR-backed notifier.
/// </summary>
internal sealed class NoOpChangeNotifier : IChangeNotifier
{
    public Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SharesChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => Task.CompletedTask;
}
