using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Ingests an inbound iMIP email into the scheduling system (ADR 0056): parses the text/calendar
/// part and routes by METHOD, resolving addresses to local active users. Shared by the REST
/// ingestion endpoint and the IMAP poller. Reuses the schedule-inbox + object store; the recipient
/// is derived from the iTIP content (ATTENDEE / ORGANIZER), not the untrusted envelope.
/// </summary>
internal sealed class InboundItipProcessor(
    SimplCalConDbContext dbContext,
    IScheduleInboxRepository inboxes,
    IObjectStore objectStore,
    ILogger<InboundItipProcessor> logger) : IInboundItipProcessor
{
    public async Task<InboundItipResult> ProcessAsync(string rawMimeMessage, CancellationToken cancellationToken)
    {
        var ics = ExtractCalendar(rawMimeMessage);
        if (ics is null)
        {
            return new InboundItipResult(InboundItipOutcome.NoCalendarPart);
        }

        if (ItipCalendar.Inspect(ics) is not { } info)
        {
            return new InboundItipResult(InboundItipOutcome.Ignored, "not a scheduling object");
        }

        var method = (ItipCalendar.ReadMethod(ics) ?? "REQUEST").ToUpperInvariant();
        logger.LogDebug("Inbound iMIP {Method} for {Uid}.", method, info.Uid);

        return method switch
        {
            "REQUEST" => await DeliverToAttendeesAsync(ics, info, "REQUEST", cancellationToken),
            "CANCEL" => await CancelAsync(ics, info, cancellationToken),
            "REPLY" => await ApplyReplyAsync(info, cancellationToken),
            _ => new InboundItipResult(InboundItipOutcome.Ignored, $"unsupported method {method}"),
        };
    }

    private async Task<InboundItipResult> DeliverToAttendeesAsync(
        string ics, ItipInfo info, string method, CancellationToken cancellationToken)
    {
        var delivered = 0;
        foreach (var attendee in info.Attendees)
        {
            if (await ResolveAsync(attendee.Email, cancellationToken) is { } recipient)
            {
                var inbox = await inboxes.EnsureInboxAsync(recipient.UserId, recipient.TenantId, cancellationToken);
                await inboxes.DeliverAsync(inbox.Id, ics, method, cancellationToken);
                delivered++;
            }
        }

        return delivered > 0
            ? new InboundItipResult(InboundItipOutcome.DeliveredToInbox, $"{delivered} recipient(s)")
            : new InboundItipResult(InboundItipOutcome.UnknownRecipient);
    }

    private async Task<InboundItipResult> CancelAsync(string ics, ItipInfo info, CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var attendee in info.Attendees)
        {
            if (await ResolveAsync(attendee.Email, cancellationToken) is { } recipient)
            {
                var inbox = await inboxes.EnsureInboxAsync(recipient.UserId, recipient.TenantId, cancellationToken);
                await inboxes.DeliverAsync(inbox.Id, ics, "CANCEL", cancellationToken);
                await RemoveByUidAsync(recipient.UserId, info.Uid, cancellationToken);
                affected++;
            }
        }

        return affected > 0
            ? new InboundItipResult(InboundItipOutcome.Cancelled, $"{affected} recipient(s)")
            : new InboundItipResult(InboundItipOutcome.UnknownRecipient);
    }

    private async Task<InboundItipResult> ApplyReplyAsync(ItipInfo info, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(info.OrganizerEmail, cancellationToken) is not { } organizer)
        {
            return new InboundItipResult(InboundItipOutcome.UnknownRecipient);
        }

        if (info.Attendees.FirstOrDefault() is not { } replier)
        {
            return new InboundItipResult(InboundItipOutcome.Ignored, "reply has no attendee");
        }

        await AutoApplyAsync(organizer.UserId, info.Uid, replier.Email, replier.ParticipationStatus, cancellationToken);
        return new InboundItipResult(InboundItipOutcome.AppliedReply, replier.Email);
    }

    // Apply an attendee's PARTSTAT to the local organizer's copy of the event (mirrors SchedulingService).
    private async Task AutoApplyAsync(
        Guid organizerUserId, string uid, string attendeeEmail, string partStat, CancellationToken cancellationToken)
    {
        var calendarIds = await dbContext.Calendars
            .Where(c => c.OwnerId == organizerUserId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var target = await dbContext.CalendarObjects.AsNoTracking()
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

    private async Task RemoveByUidAsync(Guid userId, string uid, CancellationToken cancellationToken)
    {
        var calendarIds = await dbContext.Calendars
            .Where(c => c.OwnerId == userId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var target = await dbContext.CalendarObjects.AsNoTracking()
            .Where(o => calendarIds.Contains(o.CollectionId) && o.Uid == uid && !o.IsDeleted)
            .Select(o => new { o.CollectionId, o.ResourceName })
            .FirstOrDefaultAsync(cancellationToken);
        if (target is not null)
        {
            await objectStore.DeleteAsync(target.CollectionId, target.ResourceName, userId, cancellationToken);
        }
    }

    private async Task<Recipient?> ResolveAsync(string email, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.NormalizedEmail == email.ToUpperInvariant() && u.Status == UserStatus.Active && u.TenantId != null)
            .Select(u => new Recipient(u.Id, u.TenantId!.Value))
            .FirstOrDefaultAsync(cancellationToken);

    private static string? ExtractCalendar(string rawMimeMessage)
    {
        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawMimeMessage));
            message = MimeMessage.Load(stream);
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            var isCalendar = part.ContentType.MimeType.Equals("text/calendar", StringComparison.OrdinalIgnoreCase)
                || (part.FileName?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!isCalendar)
            {
                continue;
            }

            if (part is TextPart text)
            {
                return text.Text;
            }

            if (part.Content is null)
            {
                continue;
            }

            using var content = new MemoryStream();
            part.Content.DecodeTo(content);
            return Encoding.UTF8.GetString(content.ToArray());
        }

        return null;
    }

    private sealed record Recipient(Guid UserId, Guid TenantId);
}
