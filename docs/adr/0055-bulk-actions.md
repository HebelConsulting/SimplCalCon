# ADR 0055 — Bulk move/delete for events & contacts

## Status

Accepted — implemented. Closes the ADR 0042 deferred "bulk actions".

## Context

Single-entry move (`POST …/{id}/move`) and soft-delete (`DELETE …/{id}`) existed, but the web UI
could only act on one item at a time. Cleaning up or reorganising a calendar/address book meant N
clicks. ADR 0042 deferred multi-select bulk actions.

## Decision

Add **bulk verb sub-resources** on the events/contacts collections and multi-select to the list UIs.

- **Endpoints** (a genuine state transition over a set, so a `POST` verb sub-resource per ADR 0009,
  like `move`): `POST …/events/bulk-delete {ids}` and `POST …/events/bulk-move {ids, targetId}`
  (and the contacts equivalents). Each returns a **`BulkResultResource {succeeded, failed,
  failures[]}`** — partial failure is reported, not fatal (a missing id or a UID clash on move is
  counted, the rest proceed). `write-content` is checked once (on both collections for move).
- **If-Match-exempt.** Operating on a set with per-item ETags is impractical, so bulk endpoints act
  on the **current versions** (a deliberate deviation, consistent with restore/split/rename which
  use `If-Match: *`). The UI acts on what's displayed.
- **iTIP.** Bulk-deleting events fires the normal per-event scheduling (organizer → CANCEL), so
  attendees are notified — consistent with the single delete (ADR 0031/0053).
- **UI.** A checkbox column + "select all (filtered)" header in the Calendar **list** view and the
  Contacts list, and a selection toolbar (N selected · Move to… · Delete · Clear) with an aggregate
  result message. Row checkboxes `stopPropagation` so they don't open the detail. Live updates
  (ADR 0049) refresh both views after the bulk write.

No schema change, no new dependency — the endpoints loop the existing `IObjectStore` write path.

## Consequences

- Users can move/delete many entries in one action; the result summarises partial failures.
- Each item is still an individual `IObjectStore` write (revision, tombstone, change-sequence bump,
  live-update signal) — the client debounces the resulting notifications (ADR 0049).

## Simplifications / deferred

- Bulk actions are **web-list only** (calendar list + contacts list); the calendar grid and the DAV
  surface are unchanged.
- No bulk restore/purge from trash, no cross-tenant move (blocked by the same tenant rule as the
  single move), and no undo beyond the existing trash.
