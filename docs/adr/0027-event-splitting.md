# ADR 0027 — Event splitting (as built)

## Status
Accepted (2026-07-23, Phase 1 implementation).

## Context
Users want to carve a running event into two parts at a chosen point in time — e.g. an
event was interrupted, and the remainder should become its own entry. The requested
shape: a copy of the item, with the **first half taking an earlier end** and the
**second half a later start**.

## Decision
Split one event at an instant **T** into **two same-kind events in the same calendar**
(interview outcome — no new "backlog" collection type):

- the **original** keeps its UID and identity, with `DTEND` moved to **T**;
- a **copy** with a **fresh UID** starts at **T** and keeps the original end.

**REST (ADR 0009).** `POST /api/calendars/{calendarId}/events/{id}/split` with
`{ "atUtc": "…Z" }` — a genuine state transition, so a verb sub-resource is justified.
Requires `write-content` (ADR 0007) and carries the ETag/If-Match precondition
(`[RequireIfMatch]` → 428 if absent, 412 on a stale token against the original). Returns
`200` with a `SplitEventResource` (`original` + `created`, plus a `created` link).

**Preconditions** are validated in the controller from the object's already-extracted
fields (no blob parse in the Api), each mapped to a stable error code (all `400`):
- recurring (`IsRecurring`) → `CANNOT_SPLIT_RECURRING` (recurrence splitting is deferred);
- all-day, non-event, or no start/end window → `EVENT_NOT_SPLITTABLE`;
- `atUtc` not strictly inside `(start, end)` → `SPLIT_POINT_OUT_OF_RANGE`.

These live in a new Api exception area `Errors/Exceptions/Calendars/` (base
`CalendarException`), per the two-level hierarchy rule.

**Blob-preserving transform (Infrastructure, ADR 0003/0004).** The split keeps the
**full blob** on each half — only `DTSTART`/`DTEND` move — so description, location,
attendees, etc. survive. This is deliberately **not** routed through `IObjectComposer`
(which rebuilds from structured fields and would drop everything but summary/start/end).
`CalendarObjectParser.SplitEventAt` (Ical.Net, two independent loads) produces the
truncated original and the tail copy; the new `IEventSplitter` (Application port,
`EventSplitter` impl) writes both through `IObjectStore` so each gets a revision, ETag,
and change-sequence bump. The Api never references Ical.Net.

**Web UI (ADR 0025).** The agenda view shows a **Split** control on each timed,
non-recurring event; it pre-fills the event midpoint and posts the split
(`If-Match: *` — the UI splits the current version).

## Consequences
- **Verified**: 64 tests (25 unit + 39 integration). Split tests cover the happy path
  (two contiguous events, original id kept, new copy id), **full-blob preservation on
  both halves** (DESCRIPTION/LOCATION survive), and rejections for recurring, all-day,
  out-of-range, and missing If-Match.
- **Non-atomic across the two writes**: `IObjectStore.PutAsync` opens its own transaction
  per object, so the split is two sequential writes. The **copy is created first**, so a
  failure between them leaves a momentary overlap (recoverable) rather than losing the
  tail. A single-transaction split is a future refinement.
- **Deferred**: splitting recurring events (which occurrence? split the rule?), splitting
  tasks/VTODO (no REST tasks resource yet), and an N-way split. The copy inherits the
  original `DTSTAMP`; a fresh stamp is a minor future nicety.
