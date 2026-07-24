using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Scheduling;

namespace SimplCalCon.Application.Abstractions.Scheduling;

/// <summary>
/// Storage for CalDAV schedule-inboxes and their iTIP messages (RFC 6638, ADR 0031):
/// per-user inbox provisioning, message delivery, and the read/sync/delete access the
/// DAV inbox surface needs. The inbox's <see cref="Collection.ChangeSequence"/> backs
/// its CTag and sync-token.
/// </summary>
public interface IScheduleInboxRepository
{
    /// <summary>Returns the owner's schedule-inbox, creating it on first access.</summary>
    Task<ScheduleInbox> EnsureInboxAsync(Guid ownerId, Guid tenantId, CancellationToken cancellationToken);

    Task<ScheduleInbox?> GetInboxAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Delivers a message into an inbox (new resource + change-sequence bump).</summary>
    Task DeliverAsync(Guid inboxId, string blob, string method, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleMessage>> ListMessagesAsync(Guid inboxId, CancellationToken cancellationToken);

    Task<ScheduleMessage?> GetMessageAsync(Guid inboxId, string resourceName, CancellationToken cancellationToken);

    /// <summary>Tombstones a message (drained by the client), so sync reports the removal. False if absent.</summary>
    Task<bool> DeleteMessageAsync(Guid inboxId, string resourceName, CancellationToken cancellationToken);

    Task<ScheduleInboxSyncResult> SyncAsync(Guid inboxId, long? sinceToken, CancellationToken cancellationToken);
}

public sealed record ScheduleInboxSyncResult(
    IReadOnlyList<ScheduleMessage> Changed, IReadOnlyList<string> RemovedResourceNames, long Token);
