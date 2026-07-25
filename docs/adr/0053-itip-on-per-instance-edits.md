# ADR 0053 — iTIP scheduling on per-instance recurrence edits

## Status

Accepted — implemented. Completes a deferred item of ADR 0051.

## Context

ADR 0051 added REST per-instance recurring edits (override one occurrence, exclude one via EXDATE,
truncate/split "this and following") but explicitly **did not send iTIP** — attendees weren't
notified when the organizer changed a single occurrence. (The DAV path was already covered: a
native client PUTs the whole modified blob, which `CalDavObjectController` runs through
`ISchedulingService.ProcessWriteAsync`.) The REST endpoints bypassed scheduling.

## Decision

Hook the existing `ISchedulingService.ProcessWriteAsync` into the REST per-instance endpoints
(`EventsController.UpdateOccurrence` / `DeleteOccurrence`), passing the pre-edit and post-edit
**whole-object** blobs — exactly as the whole-series `Update` already does.

- Because `OrganizerWriteAsync` emits a `METHOD:REQUEST` to every attendee on **any** organizer
  modification (not only attendee-set changes), a per-instance change delivers a REQUEST carrying
  the updated object (master + the RECURRENCE-ID override, or the master with the added EXDATE /
  shortened RRULE). Attendees' clients apply it and the instance moves/disappears.
- **Exclude/truncate is a modification, not a cancellation** — the object still exists — so it fires
  a REQUEST (reflecting the EXDATE / shortened rule), **not** a full `ProcessDeleteAsync` CANCEL.
- **"This and following" edit** produces two writes, each invited: the shortened old series
  (`ProcessWriteAsync(old, truncated)`) and the new series (`ProcessWriteAsync(null, newSeries)`).
- Delivery reuses everything from ADRs 0031/0045/0047: REQUEST to each local attendee's
  schedule-inbox, and iMIP email to external attendees (per-tenant SMTP).

No new dependency, no schema change.

## Consequences

- Organizer per-instance edits/deletes from the web/REST now notify attendees, consistent with the
  whole-series behaviour and with the DAV path.
- Editing one occurrence re-delivers a REQUEST to attendees (same UX as editing the whole event).

## Simplifications / deferred

- The **whole-object REQUEST** is used rather than a precise per-instance `CANCEL`/`REQUEST` with a
  bare `RECURRENCE-ID`; it's valid iTIP (RFC 5546 allows master + overrides) and matches the
  tenant-internal auto-apply model, but is coarser than a minimal single-instance message.
- The override VEVENT still carries only the changed fields, **not** its own `ORGANIZER`/`ATTENDEE`
  (they're on the master within the same REQUEST) — fine for lenient clients; a per-component copy
  is a possible refinement.
- **Attendee** per-instance actions remain quiet (an attendee declining a single occurrence →
  `REPLY;PARTSTAT=DECLINED;RECURRENCE-ID` is not yet sent; only whole-object PARTSTAT changes reply).
