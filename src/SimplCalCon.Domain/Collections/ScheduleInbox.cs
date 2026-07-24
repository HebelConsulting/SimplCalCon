using SimplCalCon.Domain.Scheduling;

namespace SimplCalCon.Domain.Collections;

/// <summary>
/// A user's CalDAV schedule-inbox (RFC 6638, ADR 0031): the collection that receives
/// delivered iTIP scheduling messages (REQUEST/REPLY/CANCEL). Auto-provisioned per user
/// at <c>/dav/calendars/{userId}/inbox/</c>; reuses <see cref="Collection.ChangeSequence"/>
/// so native clients get a CTag + sync-collection. Holds <see cref="ScheduleMessage"/>s
/// rather than calendar objects.
/// </summary>
public class ScheduleInbox : Collection
{
    public ICollection<ScheduleMessage> Messages { get; set; } = [];
}
