namespace SimplCalCon.Application.Abstractions;

/// <summary>
/// Pushes change signals to connected clients for live updates (ADR 0049). Fired from the
/// write path (<see cref="Storage.IObjectStore"/>) and the schedule-inbox after the DB
/// transaction commits, so a client that reloads sees the committed state. The implementation
/// (SignalR, in the Api) must never throw back into the caller — a push failure must not fail
/// a write. The default <c>NoOpChangeNotifier</c> is used where no transport is wired.
/// </summary>
public interface IChangeNotifier
{
    /// <summary>A collection's contents changed (an object was written, restored, or deleted).</summary>
    Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>A user's schedule-inbox changed (an invitation arrived or was drained).</summary>
    Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken);
}
