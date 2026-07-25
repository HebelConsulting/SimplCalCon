namespace SimplCalCon.Application.Abstractions.Scheduling;

/// <summary>
/// Processes an inbound iMIP email (RFC 6047) into the scheduling system (ADR 0056): parse the
/// <c>text/calendar</c> part and route by METHOD — REQUEST/CANCEL to each local attendee's
/// schedule-inbox (CANCEL also removes the matching event), REPLY applies the sender's PARTSTAT to
/// the local organizer's copy. Shared by the REST ingestion endpoint and the IMAP poller.
/// </summary>
public interface IInboundItipProcessor
{
    Task<InboundItipResult> ProcessAsync(string rawMimeMessage, CancellationToken cancellationToken);
}

public enum InboundItipOutcome
{
    DeliveredToInbox,
    AppliedReply,
    Cancelled,
    NoCalendarPart,
    UnknownRecipient,
    Ignored,
}

public sealed record InboundItipResult(InboundItipOutcome Outcome, string? Detail = null);
