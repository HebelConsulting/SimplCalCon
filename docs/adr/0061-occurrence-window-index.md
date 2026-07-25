# ADR 0061 — Occurrence-window index (materialized recurrence expansion)

## Status

Accepted — implemented. Completes the ADR 0004/0020/0022 deferred "occurrence-window index (materialized)".

## Context

Calendar time-range queries (CalDAV `calendar-query`, the REST agenda) must decide which recurring
events overlap a window. Until now a recurring master is expanded **on the fly** per request with
Ical.Net (`CalendarOccurrence.OverlapsRange`) after a SQL pre-filter to recurring candidates. Correct
and simple, but the expansion cost scales with the number of recurring events × occurrences per
request — the last standing performance item at the ADR 0014 medium-scale target.

The hard part: RRULEs can be **unbounded** (no `UNTIL`/`COUNT`), so no index can hold "all"
occurrences. Any materialization is a **bounded window**, which forces a correctness fallback and a
freshness mechanism.

## Decision

Materialize each recurring event's occurrences into an indexed table over a **rolling window**, use it
to answer time-range overlap in SQL, and **fall back to on-the-fly expansion** whenever a query
reaches beyond an object's materialized window — so the index is a **pure acceleration** that can
never change results.

### Schema

- **`EventOccurrences`** table — one row per materialized occurrence of a **recurring event**:
  `Id`, `ObjectId` (FK → `Objects`, cascade), `CollectionId` (denormalized), `StartUtc`, `EndUtc`.
  Indexes: `(CollectionId, StartUtc)` (the range scan) + `(ObjectId)`. Non-recurring events are served
  from their existing `DtStartUtc`/`DtEndUtc` columns and get **no** rows.
- **`CalendarObject`** gains window-coverage flags: `OccurrencesComplete` (bool, DB default **false**),
  `OccurrencesFromUtc?`, `OccurrencesUntilUtc?`. `Complete` = every occurrence is materialized
  (non-recurring, or a bounded rule wholly inside the window); otherwise the pair bounds what is
  covered. Migrations regenerated for **both** providers.

### Materialization (`OccurrenceIndexer`, Infrastructure)

On every write, for a recurring **VEVENT**: delete the object's rows and re-expand
`[now-PastDays, now+FutureDays]` via `CalendarOccurrence.Materialize` (Ical.Net), capped at
`MaxRowsPerObject` for pathological rules. Coverage flags: past-complete when `DTSTART ≥ from`,
future-complete when the series ends before the window edge (not truncated); if the row cap stopped
us, `Until` is the last materialized start (only that far is safe).

- **Write path** (`ObjectStore.CommitObjectAsync`): runs **before** the object's `SaveChanges`, inside
  the same transaction, so object + rows + a **single** ETag regeneration commit atomically (a second
  save would bump the ETag again and desync it from the recorded revision).
- **Soft-delete** removes the rows (a later restore rebuilds them); **purge** cascades.

### Query (`DavRepository.QueryOverlappingAsync`, used by both `calendar-query` paths)

For a **bounded** range: non-recurring column overlap **+** recurring objects whose window covers the
query answered purely in SQL via an occurrence-index `EXISTS`; recurring objects **not** covered fall
back to `CalendarOccurrence.OverlapsRange`. A **half-open/unbounded** range can't be narrowed for
recurring objects, so it keeps the lenient old behavior (include all recurring). The `EXISTS` uses the
same **start-based** overlap semantics as `OverlapsRange`, so index and fallback agree — asserted by a
test that compares repository results against pure expansion across in-window, beyond-window, past, and
far-future ranges.

### Roll-forward (`OccurrenceRollForwardService`, `BackgroundService`, on by default)

As real time advances an object's future horizon ages. This sweep re-materializes incomplete objects
whose coverage has dropped within `RefreshBelowDays` of "now" (and backfills never-materialized rows,
e.g. events predating the migration), in batched transactions. It updates the flag columns via
`ExecuteUpdate` so it does **not** bump the object's ETag/concurrency token — an internal refresh is
not an edit (a bump would cause spurious `If-Match` 412s) — and never touches the collection change
sequence. Correctness never depends on it running; it only keeps the fast path fresh.

Config `SimplCalCon:Occurrences`: `PastDays` (365), `FutureDays` (730), `MaxRowsPerObject` (2000),
`RollForwardEnabled` (true), `RollForwardHours` (24), `RollForwardBatch` (200), `RefreshBelowDays`
(365). Defaults work out of the box.

## Consequences

- In-window time-range queries (the common case) skip per-request recurrence expansion — an indexed
  range scan instead. Beyond-window queries are exactly as before (fallback), never wrong.
- Every recurring write now also materializes a bounded set of rows (capped); a daily event over the
  default 2-year horizon is ~730 rows. Storage grows with recurring-event density — acceptable at the
  medium-scale target.
- The blob remains the source of truth (ADR 0004); the index is derived and fully rebuildable.

## Deferred

- Feeding the index into the **grid occurrence expansion** (`QueryCalendarOccurrencesAsync`), which
  needs per-occurrence summary/location for overrides — it stays on Ical.Net (already window-bounded
  and cheap). The index accelerates the **overlap** queries only.
- Free/busy and `addressbook-query` are unaffected.
