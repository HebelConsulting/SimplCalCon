using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Common;

namespace SimplCalCon.Domain.Scheduling;

/// <summary>
/// One delivered iTIP scheduling message in a <see cref="ScheduleInbox"/> (RFC 6638,
/// ADR 0031). The <see cref="Blob"/> (a VCALENDAR with a METHOD) is the source of truth;
/// native clients GET and DELETE these, and sync-collection reports them via
/// <see cref="ChangeNumber"/> + the tombstone. Delivered by the server on the organizer/
/// attendee scheduling actions — never PUT directly by clients.
/// </summary>
public class ScheduleMessage : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    /// <summary>The owning <see cref="ScheduleInbox"/>.</summary>
    public Guid CollectionId { get; set; }

    public ScheduleInbox? Inbox { get; set; }

    /// <summary>Inbox item name (e.g. <c>{guid}.ics</c>), unique within the inbox.</summary>
    public required string ResourceName { get; set; }

    /// <summary>The iTIP VCALENDAR payload (carries METHOD:REQUEST/REPLY/CANCEL).</summary>
    public required string Blob { get; set; }

    /// <summary>Extracted iTIP method (REQUEST/REPLY/CANCEL), for filtering.</summary>
    public required string Method { get; set; }

    /// <summary>The inbox's <see cref="Collections.Collection.ChangeSequence"/> at this message's last change.</summary>
    public long ChangeNumber { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Tombstone: a drained (DELETEd) message is retained so sync can report the removal.</summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
