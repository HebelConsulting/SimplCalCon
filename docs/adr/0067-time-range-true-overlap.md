# ADR 0067 — Time-range matching: true RFC 4791 overlap

## Status

Accepted — implemented. Fixes a correctness caveat in ADR 0022/0061 (and ADR 0030 free/busy).

## Context

`calendar-query` time-range matching (and free/busy, and the occurrence-window index) matched a
recurring occurrence when its **start** fell inside the range. RFC 4791 defines time-range as **true
interval overlap**: an occurrence `[s, e)` matches `[start, end)` iff `s < end && e > start`. The
start-based approximation **missed** an occurrence that began *before* a client's visible window but
ran *into* it — e.g. a multi-day or all-day event, refreshed while the window is mid-event. Every
native client (Apple Calendar, DAVx5, Thunderbird) issues time-range REPORTs when refreshing a view,
so the gap was user-visible. (Non-recurring events were already correct — their extracted
`DtStartUtc`/`DtEndUtc` columns are compared as a true overlap.)

While fixing it, a second latent bug surfaced: **Ical.Net populates `Occurrence.Period.EndTime` as
null** — the effective end is on `Period.EffectiveEndTime`. Every path that read `EndTime ?? start`
was silently treating recurring occurrences as **zero-duration** (so recurring events never counted
as busy, `calendar-data expand` emitted end-less instances, and grid occurrences had no length).

## Decision

- **True overlap** everywhere a time-range is evaluated for recurring events, with the same inclusive
  effective-end semantics the non-recurring column filter already uses (`s < end && e >= start`, which
  also handles point events):
  - `CalendarOccurrence.OverlapsRange` (the fallback) and `Materialize` (the index) expand from
    `windowStart − maxEventDuration` (a **look-back** bounded by the event's own duration, so a
    start-ordered scan can stop at `s >= end` yet not skip a spanning occurrence) and keep/return
    occurrences overlapping the window.
  - `DavRepository.QueryOverlappingAsync` occurrence-index `EXISTS` uses `StartUtc < end && EndUtc >= start`.
  - `FreeBusyService` expands with the same look-back and overlap.
- **Occurrence end** reads `Period.EffectiveEndTime` (not the null `EndTime`) in overlap, the index,
  free/busy, the grid expansion, and `calendar-data expand` — so occurrences carry their real length.

Index and fallback stay in lock-step (same predicate, same effective-end), preserving the ADR 0061
"index == fallback == expansion" invariant — now on true-overlap semantics.

## Consequences

- A long/all-day/multi-day event is returned by a time-range REPORT whenever it overlaps the window,
  not only when it starts in it — the documented native-client caveat is resolved.
- Recurring events now correctly contribute busy time to free/busy, and `calendar-data expand`
  instances keep their duration.
- The look-back is exact for uniform-duration series; an override longer than the master is bounded by
  the max master duration in the blob (a rare edge, and lenient rather than missing).

## Deferred

- Per-occurrence override durations exceeding the max master duration (pathological); `limit-recurrence-set`.
