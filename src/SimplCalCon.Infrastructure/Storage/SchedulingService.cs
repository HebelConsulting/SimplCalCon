using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// RFC 6638 server-side automatic scheduling (ADR 0031). Local recipients get an inbox delivery;
/// external recipients get an iMIP email when the tenant has SMTP configured (ADR 0047).
/// </summary>
internal sealed class SchedulingService(
    SimplCalConDbContext dbContext,
    IScheduleInboxRepository inboxes,
    IObjectStore objectStore,
    ITenantEmailSettingsService emailSettings,
    IEmailSender emailSender,
    ILogger<SchedulingService> logger) : ISchedulingService
{
    public async Task ProcessWriteAsync(
        Guid collectionId, string? oldBlob, string newBlob, Guid actingUserId, CancellationToken cancellationToken)
    {
        if (ItipCalendar.Inspect(newBlob) is not { } info)
        {
            return;
        }

        var actor = await ActorAsync(actingUserId, cancellationToken);
        if (actor is null)
        {
            return;
        }

        if (info.OrganizerEmail == actor.Email)
        {
            await OrganizerWriteAsync(info, oldBlob, newBlob, actor.TenantId, cancellationToken);
        }
        else if (info.Attendees.FirstOrDefault(a => a.Email == actor.Email) is { } mine)
        {
            await AttendeeReplyAsync(info, oldBlob, actor, mine, cancellationToken);
        }
    }

    public async Task ProcessDeleteAsync(
        Guid collectionId, string deletedBlob, Guid actingUserId, CancellationToken cancellationToken)
    {
        if (ItipCalendar.Inspect(deletedBlob) is not { } info)
        {
            return;
        }

        var actor = await ActorAsync(actingUserId, cancellationToken);
        if (actor is null || info.OrganizerEmail != actor.Email)
        {
            return; // Only the organizer cancelling propagates in this slice.
        }

        var cancel = ItipCalendar.Cancel(deletedBlob);
        var recipients = info.Attendees.Where(a => a.Email != actor.Email).ToList();
        logger.LogInformation(
            "Scheduling CANCEL for {Uid} from {Organizer} to {AttendeeCount} attendee(s).",
            info.Uid, actor.Email, recipients.Count);
        foreach (var attendee in recipients)
        {
            await DeliverAsync(attendee.Email, actor.TenantId, cancel, "CANCEL", actor.Email, cancellationToken);
        }
    }

    public async Task SendReplyAsync(
        Guid attendeeUserId, string requestBlob, string participationStatus, CancellationToken cancellationToken)
    {
        if (ItipCalendar.Inspect(requestBlob) is not { } info)
        {
            return;
        }

        var actor = await ActorAsync(attendeeUserId, cancellationToken);
        if (actor is null || info.Attendees.FirstOrDefault(a => a.Email == actor.Email) is not { } mine)
        {
            return;
        }

        logger.LogInformation(
            "Scheduling REPLY for {Uid} from {Attendee} ({PartStat}) to organizer {Organizer} (REST).",
            info.Uid, actor.Email, participationStatus, info.OrganizerEmail);

        var reply = ItipCalendar.Reply(info.Uid, info.Organizer, mine.Address, participationStatus, mine.CommonName);
        var organizer = await DeliverAsync(info.OrganizerEmail, actor.TenantId, reply, "REPLY", actor.Email, cancellationToken);
        if (organizer is { } organizerUserId)
        {
            await AutoApplyAsync(organizerUserId, info.Uid, actor.Email, participationStatus, cancellationToken);
        }
    }

    private async Task OrganizerWriteAsync(
        ItipInfo info, string? oldBlob, string newBlob, Guid tenantId, CancellationToken cancellationToken)
    {
        // REQUEST to every current (local) attendee except the organizer.
        var request = ItipCalendar.Request(newBlob);
        var recipients = info.Attendees.Where(a => a.Email != info.OrganizerEmail).ToList();
        logger.LogInformation(
            "Scheduling REQUEST for {Uid} from {Organizer} to {AttendeeCount} attendee(s).",
            info.Uid, info.OrganizerEmail, recipients.Count);
        foreach (var attendee in recipients)
        {
            await DeliverAsync(attendee.Email, tenantId, request, "REQUEST", info.OrganizerEmail, cancellationToken);
        }

        // CANCEL to attendees present before but removed now.
        if (oldBlob is not null && ItipCalendar.Inspect(oldBlob) is { } previous)
        {
            var current = info.Attendees.Select(a => a.Email).ToHashSet();
            var cancel = ItipCalendar.Cancel(newBlob);
            foreach (var removed in previous.Attendees
                .Where(a => a.Email != info.OrganizerEmail && !current.Contains(a.Email)))
            {
                await DeliverAsync(removed.Email, tenantId, cancel, "CANCEL", info.OrganizerEmail, cancellationToken);
            }
        }
    }

    private async Task AttendeeReplyAsync(
        ItipInfo info, string? oldBlob, Actor actor, ItipAttendee mine, CancellationToken cancellationToken)
    {
        // Only reply when the attendee's PARTSTAT actually changed (a first accept counts).
        var previous = oldBlob is not null
            ? ItipCalendar.Inspect(oldBlob)?.Attendees.FirstOrDefault(a => a.Email == actor.Email)?.ParticipationStatus
            : null;
        if (previous == mine.ParticipationStatus)
        {
            return;
        }

        logger.LogInformation(
            "Scheduling REPLY for {Uid} from {Attendee} ({PartStat}) to organizer {Organizer}.",
            info.Uid, actor.Email, mine.ParticipationStatus, info.OrganizerEmail);

        var reply = ItipCalendar.Reply(info.Uid, info.Organizer, mine.Address, mine.ParticipationStatus, mine.CommonName);
        var organizer = await DeliverAsync(info.OrganizerEmail, actor.TenantId, reply, "REPLY", actor.Email, cancellationToken);
        if (organizer is { } organizerUserId)
        {
            await AutoApplyAsync(organizerUserId, info.Uid, actor.Email, mine.ParticipationStatus, cancellationToken);
        }
    }

    // Update the organizer's own copy of the event so its ATTENDEE PARTSTAT reflects the reply (ADR 0031: auto-apply).
    private async Task AutoApplyAsync(
        Guid organizerUserId, string uid, string attendeeEmail, string partStat, CancellationToken cancellationToken)
    {
        var calendarIds = await dbContext.Calendars
            .Where(c => c.OwnerId == organizerUserId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // No-tracking lookup: ObjectStore.PutAsync loads + tracks its own copy, so pre-tracking
        // the same row here would corrupt its concurrency OriginalValue.
        var target = await dbContext.CalendarObjects
            .AsNoTracking()
            .Where(o => calendarIds.Contains(o.CollectionId) && o.Uid == uid && !o.IsDeleted)
            .Select(o => new { o.CollectionId, o.ResourceName, o.Blob })
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return;
        }

        var updated = ItipCalendar.ApplyPartStat(target.Blob, attendeeEmail, partStat);
        if (updated != target.Blob)
        {
            await objectStore.PutAsync(
                new PutObjectRequest(target.CollectionId, target.ResourceName, updated, organizerUserId), cancellationToken);
        }
    }

    // Local recipient → schedule-inbox (returns their id so a REPLY can auto-apply); external
    // recipient → iMIP email when the tenant has SMTP configured (ADR 0047), else logged/dropped.
    private async Task<Guid?> DeliverAsync(
        string recipientEmail, Guid tenantId, string blob, string method, string replyToEmail, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(recipientEmail, tenantId, cancellationToken) is { } recipient)
        {
            var inbox = await inboxes.EnsureInboxAsync(recipient, tenantId, cancellationToken);
            await inboxes.DeliverAsync(inbox.Id, blob, method, cancellationToken);
            logger.LogDebug("Delivered {Method} to the schedule inbox of {Email}.", method, recipientEmail);
            return recipient;
        }

        if (await emailSettings.GetSendConfigAsync(tenantId, cancellationToken) is { } smtp)
        {
            await emailSender.SendItipAsync(smtp, BuildItipMail(recipientEmail, replyToEmail, blob, method), cancellationToken);
            logger.LogInformation("Sent iMIP {Method} email to external {Email}.", method, recipientEmail);
        }
        else
        {
            logger.LogDebug("No local recipient or tenant SMTP for {Email}; {Method} not delivered.", recipientEmail, method);
        }

        return null;
    }

    private static ItipMail BuildItipMail(string to, string replyTo, string blob, string method)
    {
        var summary = ItipCalendar.Inspect(blob)?.Summary ?? "(event)";
        var (subject, text) = method switch
        {
            "REQUEST" => ($"Invitation: {summary}", $"You have been invited to \"{summary}\". Open the attached calendar item to respond."),
            "CANCEL" => ($"Cancelled: {summary}", $"\"{summary}\" has been cancelled."),
            "REPLY" => ($"Response: {summary}", $"An attendee has responded to \"{summary}\"."),
            _ => ($"Scheduling: {summary}", summary),
        };

        return new ItipMail(to, replyTo, subject, text, blob, method);
    }

    private async Task<Guid?> ResolveAsync(string email, Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.TenantId == tenantId && u.NormalizedEmail == email.ToUpperInvariant() && u.Status == UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Actor?> ActorAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.Id == userId && u.TenantId != null)
            .Select(u => new Actor(u.Email.ToLower(), u.TenantId!.Value))
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record Actor(string Email, Guid TenantId);
}
