using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>REST/UI view of the schedule-inbox: list + respond to invitations (ADR 0045).</summary>
internal sealed class InvitationService(
    SimplCalConDbContext dbContext,
    IScheduleInboxRepository inboxes,
    IObjectStore objectStore,
    IDavRepository repository,
    ISchedulingService scheduling) : IInvitationService
{
    public async Task<IReadOnlyList<Invitation>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var inbox = await inboxes.GetInboxAsync(userId, cancellationToken);
        if (inbox is null)
        {
            return [];
        }

        var messages = await inboxes.ListMessagesAsync(inbox.Id, cancellationToken);
        var invitations = new List<Invitation>();
        foreach (var message in messages.Where(m => m.Method == "REQUEST"))
        {
            if (ItipCalendar.Inspect(message.Blob) is { } info)
            {
                invitations.Add(new Invitation(
                    message.ResourceName, info.Uid, info.Summary, info.StartUtc, info.EndUtc, info.OrganizerEmail, null));
            }
        }

        return invitations.OrderBy(i => i.StartUtc ?? DateTime.MaxValue).ToList();
    }

    public async Task<bool> RespondAsync(
        Guid userId, string resourceName, InvitationResponse response, CancellationToken cancellationToken)
    {
        var inbox = await inboxes.GetInboxAsync(userId, cancellationToken);
        if (inbox is null)
        {
            return false;
        }

        var message = await inboxes.GetMessageAsync(inbox.Id, resourceName, cancellationToken);
        if (message is null || message.Method != "REQUEST")
        {
            return false;
        }

        var partStat = response switch
        {
            InvitationResponse.Accepted => "ACCEPTED",
            InvitationResponse.Tentative => "TENTATIVE",
            _ => "DECLINED",
        };

        // Accept/tentative also drop the event into the user's default calendar with their PARTSTAT.
        if (response is InvitationResponse.Accepted or InvitationResponse.Tentative
            && ItipCalendar.Inspect(message.Blob) is { } info)
        {
            var me = await dbContext.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Email, u.TenantId })
                .FirstOrDefaultAsync(cancellationToken);
            if (me?.TenantId is { } tenantId
                && await repository.EnsureDefaultCalendarAsync(userId, tenantId, cancellationToken) is { } calendar)
            {
                var eventBlob = ItipCalendar.ApplyPartStat(
                    ItipCalendar.WithoutMethod(message.Blob), me.Email.ToLowerInvariant(), partStat);
                await objectStore.PutAsync(
                    new PutObjectRequest(calendar.Id, $"{info.Uid}.ics", eventBlob, userId), cancellationToken);
            }
        }

        // Reply to the organizer (+ auto-apply to their copy); then drain the inbox message.
        await scheduling.SendReplyAsync(userId, message.Blob, partStat, cancellationToken);
        await inboxes.DeleteMessageAsync(inbox.Id, resourceName, cancellationToken);
        return true;
    }
}
