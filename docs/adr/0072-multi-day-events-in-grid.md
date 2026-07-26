# ADR 0072 — Multi-day events span the calendar grid

## Status

Accepted — implemented. Web-UI follow-up enabled by ADR 0067 (occurrences now carry real end times).

## Context

The month/week grid (ADR 0038) placed each event chip only on its **start** day (`EventsOn(day)`
matched `start.Date == day`). A multi-day conference or a multi-day all-day event showed on day one and
then vanished — misleading. ADR 0067 fixed occurrences to carry their real `EffectiveEndTime`, so the
grid can now show an event across every day it covers.

## Decision

Render a multi-day event as its normal chip **on each day it covers** (not a continuous spanning bar —
that would require a lane-packed absolute-positioned layout and a grid rewrite; chip-per-day fits the
existing CSS-grid-of-day-cells with minimal risk).

- **Coverage** (`CalendarGrid.CoversDay`, a pure helper): an event covers every day from its start day
  to its last day. The end is **exclusive at midnight** — an all-day event's DTEND date, or a timed
  event ending exactly at `00:00`, does **not** add a day; a missing/zero/negative duration covers only
  the start day. `CalendarView.EventsOn(day)` uses it.
- **Chip:** the start day shows the time (or `—` for all-day); a continuation day shows a `…` marker
  and flattens its left edge (`.chip-cont`) to read as "continues".
- **Fetch (ADR 0067 look-back):** `CalendarOccurrence.Occurrences` (the grid/`expand` expansion) now
  expands from `windowStart − maxEventDuration` and keeps occurrences whose interval **overlaps** the
  window, so an event starting just before the visible grid still appears on the days it spans.

## Consequences

- Multi-day and all-day multi-day events read correctly across the grid; the REST `events?expand`
  window is also overlap-based now (consistent with `calendar-query`).
- Chip-per-day, not a single bar — a common, legible representation; a true spanning bar remains
  possible later without changing the coverage logic.

## Deferred

- A continuous multi-column spanning bar with lane packing; an hourly day-grid (ADR 0038 deferral).
