# ADR 0030 — Phase 2 attendees & free/busy (as built)

## Status
Accepted (2026-07-23). First slice of [ADR 0008](0008-calendar-scope-and-scheduling.md)
Phase 2 scheduling. The invitation round-trip (iTIP delivery, accept/decline) is the
**next** slice.

## Context
Scheduling was sliced (interview): **attendees + free/busy first**, with **native-client
interop** as the priority, so free/busy follows the CalDAV path Apple/Thunderbird use.
Attendees are stored in an **indexed table** (schema approved before migrating).

## Decision

**Schema (approved).** New `EventAttendees` table — one row per `ORGANIZER`/`ATTENDEE`
(the blob stays source of truth; rows are rebuilt from it on every write): `Id`,
`ObjectId`→`Objects` (cascade), `Address`, `NormalizedAddress` (indexed, upper-cased),
`CommonName?`, `Role`/`ParticipationStatus` (string enums), `IsOrganizer`. Purely
additive; both provider migrations regenerated (`AddEventAttendees`).

**Extraction.** `CalendarObjectParser` pulls organizer + attendees (Ical.Net); the
organizer is modelled as a row with `IsOrganizer=true`. `ObjectStore` rebuilds the rows
inside its existing write transaction (delete + re-insert), so **both DAV PUT and REST
writes** populate the index. Calendar-object reads used by REST `Include` the attendees.

**REST.** `EventResource` exposes `attendees`; `EventWriteRequest` accepts `organizer` +
`attendees` and the composer writes `ORGANIZER`/`ATTENDEE` lines (bare email → `mailto:`).
`GET /api/free-busy?address&fromUtc&toUtc` resolves the address to a local user in the
caller's tenant and returns merged busy windows.

**Free/busy computation.** `IFreeBusyService` aggregates the **opaque busy windows across
the calendars a user owns**, recurrence expanded via Ical.Net (non-recurring events use
the indexed `DtStart/DtEnd`; recurring are expanded), then merged. Address resolution maps
`mailto:` → `User.NormalizedEmail` within the tenant.

**CalDAV native path (RFC 6638 + 4791).**
- Principal PROPFIND now advertises `calendar-user-address-set` (the user's `mailto:` +
  principal URL), `schedule-inbox-URL`, `schedule-outbox-URL`.
- The **schedule-outbox** answers a free-busy `POST` (parse the VFREEBUSY `REQUEST`,
  resolve each `ATTENDEE`, return a `schedule-response` with per-recipient VFREEBUSY) —
  the invite-time availability lookup native clients use. Inbox/outbox PROPFIND return
  their `resourcetype`.
- The **`free-busy-query` REPORT** on a calendar returns one VFREEBUSY for the range.

**Web UI.** Attendees can be added when creating an event (comma-separated emails) and are
shown per event with their PARTSTAT.

## Consequences
- **Verified**: 80 tests (25 unit + 55 integration). New `SchedulingTests` cover REST
  attendee round-trip, DAV PUT → attendee-index extraction, REST free/busy, the CalDAV
  `free-busy-query` REPORT (VFREEBUSY with the busy period), and the principal
  advertising the scheduling props + a schedule-outbox free-busy `POST` returning the
  attendee's busy time.
- **Simplifications (documented)**: `free-busy-query` on a calendar reports the calendar
  **owner's** aggregate availability; TRANSP:TRANSPARENT events are not yet excluded from
  busy; the outbox parses the small free-busy request with light line-parsing (no Ical.Net
  in the Api). Free/busy is **tenant-internal** — external/cross-tenant addresses resolve
  to "not resolved" (no availability).
- **Deferred (next slice)**: iTIP delivery — the schedule-inbox receiving
  `METHOD:REQUEST`, accept/decline writing PARTSTAT + a `REPLY` back to the organizer;
  DAV auto-scheduling on PUT; email iMIP.
