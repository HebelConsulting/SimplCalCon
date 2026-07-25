namespace SimplCalCon.Application.Abstractions.Scheduling;

/// <summary>
/// Web/REST view of a user's schedule-inbox (ADR 0045): lists pending iTIP invitations
/// (<c>METHOD:REQUEST</c>) and lets the user respond (accept / tentative / decline), which sends the
/// REPLY to the organizer and — for accept/tentative — adds the event to their default calendar.
/// A REST/UI companion to the DAV auto-scheduling of ADR 0031.
/// </summary>
public interface IInvitationService
{
    Task<IReadOnlyList<Invitation>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Responds to the inbox invitation; false if it no longer exists.</summary>
    Task<bool> RespondAsync(
        Guid userId, string resourceName, InvitationResponse response, CancellationToken cancellationToken);
}

public enum InvitationResponse
{
    Accepted,
    Tentative,
    Declined,
}

/// <summary>A pending invitation in the user's schedule-inbox.</summary>
public sealed record Invitation(
    string ResourceName,
    string Uid,
    string? Summary,
    DateTime? StartUtc,
    DateTime? EndUtc,
    string OrganizerEmail,
    string? OrganizerName);
