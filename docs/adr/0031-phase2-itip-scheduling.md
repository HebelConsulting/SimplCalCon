# ADR 0031 — Phase 2 iTIP scheduling (as built)

## Status
Accepted (2026-07-24). Second slice of [ADR 0008](0008-calendar-scope-and-scheduling.md)
Phase 2 scheduling, building on [ADR 0030](0030-phase2-attendees-and-free-busy.md).

## Context
Slice 1 delivered attendees + free/busy and advertised the RFC 6638 schedule
inbox/outbox. This slice implements the **invitation round-trip** — the interview
outcome was: **native-client fidelity** (real RFC 6638 auto-scheduling, not
auto-materialize), **tenant-internal**, **full REQUEST → REPLY → CANCEL**, **auto-apply**
replies to the organizer's copy, **/dav-only** (REST/UI deferred).

## Decision

**Schema (approved).** A new `ScheduleInbox` collection subtype (TPH under `Collection`,
no new columns) auto-provisioned per user at `/dav/calendars/{userId}/inbox/`, reusing
`Collection.ChangeSequence` for its CTag + sync-token. A new `ScheduleMessages` table
(`Id`, `CollectionId`→inbox cascade, `ResourceName`, `Blob`, `Method`, `ConcurrencyToken`,
`ChangeNumber`, `CreatedAt`, `IsDeleted`/`DeletedAt`) holds delivered iTIP messages. Both
provider migrations regenerated (`AddScheduleInbox`).

**Automatic scheduling (`ISchedulingService`, Infrastructure).** Hooked into the DAV
object write/delete path (`CalDavObjectController` PUT/DELETE), after the object is stored,
with the pre-write blob for diffing:
- **Organizer PUT** with ATTENDEEs → deliver `METHOD:REQUEST` to each **local** attendee's
  schedule-inbox; an attendee dropped since the previous version gets `METHOD:CANCEL`.
- **Attendee PUT** whose PARTSTAT changed (a first accept counts) → deliver `METHOD:REPLY`
  to the organizer's inbox **and auto-apply** the PARTSTAT onto the organizer's own copy.
- **Organizer DELETE** → `METHOD:CANCEL` to every local attendee.

Organizer vs attendee is decided by matching the acting user's email to `ORGANIZER`/
`ATTENDEE`. External/cross-tenant addresses are ignored (no iMIP yet). iTIP inspection +
message building (`ItipCalendar`) uses Ical.Net, so the Api never references it.

**Functional schedule-inbox (`CalDavScheduleController`).** PROPFIND (Depth 1 lists
messages with `calendar-data`), `sync-collection` REPORT, GET, and DELETE (tombstone →
sync reports the drain). The outbox free-busy POST + principal advertisement remain from
slice 1. Delivery/reads go through `IScheduleInboxRepository`.

## Consequences
- **Verified**: 85 tests (25 unit + 60 integration). New `SchedulingItipTests` cover the
  full round-trip — organizer PUT → REQUEST in the attendee inbox; attendee accept →
  REPLY in the organizer inbox **and** the organizer's event showing `PARTSTAT=ACCEPTED`
  (auto-apply); organizer DELETE → CANCEL; and draining a message (GET + DELETE).
  Manually validated against **Thunderbird** (native CalDAV).
- **Bug found + fixed en route**: `ObjectStore.RebuildAttendeesAsync` combined a SQL
  `ExecuteDelete` with a tracked-nav `Clear()`, re-deleting already-removed `EventAttendee`
  rows → a phantom concurrency conflict on **any** event update carrying attendees. Now it
  deletes in SQL and inserts via the context (no nav clear).
- **Deferred**: email iMIP (external attendees); REST/Blazor scheduling UI; attendee
  DELETE as an implicit decline; delegation; per-occurrence (recurring) scheduling;
  SEQUENCE/RECURRENCE-ID edge cases beyond the basic REQUEST/REPLY/CANCEL.
