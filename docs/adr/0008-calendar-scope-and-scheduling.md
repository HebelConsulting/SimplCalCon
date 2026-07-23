# ADR 0008 — Calendar scope: events + tasks; scheduling internal-first, iMIP later

## Status
Accepted (2026-07-23, spec interview)

## Context
Calendar servers vary enormously in scope. Recurring events are mandatory;
scheduling (invitations) is the single biggest complexity multiplier in CalDAV; iMIP
(e-mail invitations) additionally drags in an SMTP/e-mail-ingestion subsystem.

## Decision
- **In scope from Phase 1**: `VEVENT` with full recurrence (RRULE, RDATE, EXDATE,
  `RECURRENCE-ID` overrides), time zones by reference, all-day events, `VALARM`
  round-tripping (server never sends alarm notifications); `VTODO` with recurrence
  and completion state.
- **Phase 2 — CalDAV scheduling (RFC 6638), internal only**: organizers invite
  attendees who are principals of the **same tenant**; the server auto-processes
  iTIP (deposits invitations in attendee scheduling inboxes / their default
  calendar, propagates replies to the organizer's copy), supports
  accept/decline/tentative and **free-busy** lookup (also the ACL-independent
  free-busy REPORT between tenant members).
- **Phase 3 — iMIP (RFC 6047)**: e-mail invitations to external attendees (outbound
  SMTP, inbound reply ingestion). Deferred, but the attendee model stores external
  (mailto-only) attendees and their participation status from day one, so upgrading
  to iMIP changes delivery, not the data model.
- **Out of scope**: `VJOURNAL` (blobs preserved if written, no indexing/UI),
  `VFREEBUSY` objects as stored data (free-busy is computed), WebDAV locking.

## Consequences
- Recurrence correctness is a core competency — the occurrence-window index
  (ADR 0004) and an exhaustive recurrence test corpus (including override edge
  cases) are early deliverables.
- Scheduling touches ACL semantics (server writes into attendee collections on the
  attendee's behalf) — specced as part of Phase 2, not bolted on.
