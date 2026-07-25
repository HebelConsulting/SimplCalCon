using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Scheduling;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Schedule-inbox provisioning, iTIP delivery, and read/sync/delete (RFC 6638, ADR 0031).</summary>
internal sealed class ScheduleInboxRepository(
    SimplCalConDbContext dbContext, IClock clock, IChangeNotifier changeNotifier) : IScheduleInboxRepository
{
    public async Task<ScheduleInbox> EnsureInboxAsync(Guid ownerId, Guid tenantId, CancellationToken cancellationToken)
    {
        var inbox = await dbContext.ScheduleInboxes
            .FirstOrDefaultAsync(i => i.OwnerId == ownerId && !i.IsDeleted, cancellationToken);
        if (inbox is not null)
        {
            return inbox;
        }

        inbox = new ScheduleInbox
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = "Inbox",
            ResourceName = "inbox",
            CreatedAt = clock.UtcNow.UtcDateTime,
        };
        dbContext.ScheduleInboxes.Add(inbox);
        await dbContext.SaveChangesAsync(cancellationToken);
        return inbox;
    }

    public async Task<ScheduleInbox?> GetInboxAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await dbContext.ScheduleInboxes.FirstOrDefaultAsync(i => i.OwnerId == ownerId && !i.IsDeleted, cancellationToken);

    public async Task DeliverAsync(Guid inboxId, string blob, string method, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inbox = await dbContext.ScheduleInboxes.FirstAsync(i => i.Id == inboxId, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;

        dbContext.ScheduleMessages.Add(new ScheduleMessage
        {
            Id = Guid.NewGuid(),
            CollectionId = inbox.Id,
            ResourceName = $"{Guid.NewGuid():N}.ics",
            Blob = blob,
            Method = method,
            ChangeNumber = ++inbox.ChangeSequence,
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await NotifyInvitationsChangedAsync(inbox.OwnerId, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleMessage>> ListMessagesAsync(Guid inboxId, CancellationToken cancellationToken) =>
        await dbContext.ScheduleMessages
            .Where(m => m.CollectionId == inboxId && !m.IsDeleted)
            .OrderBy(m => m.ResourceName)
            .ToListAsync(cancellationToken);

    public async Task<ScheduleMessage?> GetMessageAsync(Guid inboxId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.ScheduleMessages
            .FirstOrDefaultAsync(m => m.CollectionId == inboxId && m.ResourceName == resourceName && !m.IsDeleted, cancellationToken);

    public async Task<bool> DeleteMessageAsync(Guid inboxId, string resourceName, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var message = await dbContext.ScheduleMessages
            .FirstOrDefaultAsync(m => m.CollectionId == inboxId && m.ResourceName == resourceName && !m.IsDeleted, cancellationToken);
        if (message is null)
        {
            return false;
        }

        var inbox = await dbContext.ScheduleInboxes.FirstAsync(i => i.Id == inboxId, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        message.IsDeleted = true;
        message.DeletedAt = now;
        message.ChangeNumber = ++inbox.ChangeSequence;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await NotifyInvitationsChangedAsync(inbox.OwnerId, cancellationToken);
        return true;
    }

    // Fire the live-update signal post-commit; a transport failure must never fail the delivery (ADR 0049).
    private async Task NotifyInvitationsChangedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        try
        {
            await changeNotifier.InvitationsChangedAsync(ownerId, cancellationToken);
        }
        catch
        {
            // Best-effort — the inbox is already committed; the badge simply refreshes on next navigation.
        }
    }

    public async Task<ScheduleInboxSyncResult> SyncAsync(Guid inboxId, long? sinceToken, CancellationToken cancellationToken)
    {
        var token = await dbContext.ScheduleInboxes
            .Where(i => i.Id == inboxId)
            .Select(i => i.ChangeSequence)
            .FirstAsync(cancellationToken);

        var changed = await dbContext.ScheduleMessages
            .Where(m => m.CollectionId == inboxId && !m.IsDeleted && (sinceToken == null || m.ChangeNumber > sinceToken))
            .ToListAsync(cancellationToken);

        var removed = sinceToken is null
            ? []
            : await dbContext.ScheduleMessages
                .Where(m => m.CollectionId == inboxId && m.IsDeleted && m.ChangeNumber > sinceToken)
                .Select(m => m.ResourceName)
                .ToListAsync(cancellationToken);

        return new ScheduleInboxSyncResult(changed, removed, token);
    }
}
