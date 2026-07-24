namespace SimplCalCon.Application.Abstractions.Scheduling;

/// <summary>
/// RFC 6638 server-side automatic scheduling (ADR 0031), tenant-internal. Invoked by the
/// DAV object write/delete path after the object is stored:
/// <list type="bullet">
/// <item>organizer PUT with ATTENDEEs → deliver METHOD:REQUEST to each local attendee's
/// schedule-inbox; a removed attendee gets METHOD:CANCEL;</item>
/// <item>attendee PUT changing their PARTSTAT → deliver METHOD:REPLY to the organizer's
/// inbox and auto-apply the PARTSTAT to the organizer's copy;</item>
/// <item>organizer DELETE → METHOD:CANCEL to every local attendee.</item>
/// </list>
/// External/cross-tenant addresses are ignored (no iMIP yet).
/// </summary>
public interface ISchedulingService
{
    /// <summary>Process a stored calendar-object write. <paramref name="oldBlob"/> is null for a create.</summary>
    Task ProcessWriteAsync(
        Guid collectionId, string? oldBlob, string newBlob, Guid actingUserId, CancellationToken cancellationToken);

    /// <summary>Process a calendar-object deletion (the blob as it was before removal).</summary>
    Task ProcessDeleteAsync(Guid collectionId, string deletedBlob, Guid actingUserId, CancellationToken cancellationToken);
}
