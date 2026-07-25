# 0045 — REST + web-UI invitations (accept / tentative / decline)

## Status

Accepted (2026-07-25).

## Context

ADR 0031 built RFC 6638 server-side scheduling, but **only over DAV**: the schedule-inbox is a DAV
collection, and a REQUEST is delivered only when an **organizer PUTs over DAV**. Web/REST users had
no way to *see* incoming invitations or *respond* to them, and an event created with attendees over
**REST** delivered no invitations at all. This closes both gaps, tenant-internally.

## Decision

- **`IInvitationService`** (`InvitationService`, Infrastructure):
  - `ListAsync` — the pending `METHOD:REQUEST` messages in the user's schedule-inbox, parsed
    (via `ItipCalendar.Inspect`, now also surfacing summary/start/end) into an `Invitation`.
  - `RespondAsync(accepted | tentative | declined)` — for **accept/tentative**, drops the event into
    the user's **default calendar** with their PARTSTAT (`ItipCalendar.WithoutMethod` +
    `ApplyPartStat` → `IObjectStore`); for **all three**, sends the `REPLY` to the organizer and
    auto-applies it to the organizer's copy via the new **`ISchedulingService.SendReplyAsync`**
    (which reuses the ADR 0031 resolve/deliver/auto-apply helpers); then **drains** the inbox message.
- **REST:** `GET`/`HEAD /api/invitations` (list) and `POST /api/invitations/respond`
  `{ resourceName, response }`.
- **Web-created events now invite:** `EventsController` create/update call
  `ISchedulingService.ProcessWriteAsync` (as the DAV object controller does), so a web user adding
  attendees actually delivers REQUESTs — making the flow end-to-end (web A invites → web B sees &
  responds).
- **Web UI:** an `/invitations` page listing pending invites with **Accept / Tentative / Decline**,
  linked from the Calendar ribbon.
- **No schema change** — reuses `ScheduleInbox` / `ScheduleMessage`.

## Consequences

- Web users can now both send (implicitly, by adding attendees) and answer invitations; accepting
  puts the event on their calendar and tells the organizer.
- **Import stays quiet** — scheduling is hooked into the interactive event endpoints, *not*
  `IObjectStore`, so bulk import of attended events does not spam REQUESTs.
- **Deferred (still):** email iMIP for external/cross-tenant attendees; attendee-delete-as-decline;
  recurring/delegation; an unread-invitation count badge in the shell.
