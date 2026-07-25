# ADR 0050 — Recurring-event editing (web UI)

## Status

Accepted — implemented.

## Context

The web client could create only a minimal event (summary, start, attendees) and had **no edit
form at all**; recurrence was unsettable, and recurring events showed **only at their first
occurrence** in the grid (flagged `↻`, ADR 0038). "Repeats every…" was the biggest visible gap in
the calendar UX. The composer rebuilds a REST-authored VEVENT from structured fields (it is
deliberately lossy for REST objects), so any recurrence must round-trip through structured fields.

## Decision

Build a proper **event editor** (shared create + edit) that includes recurrence, and **expand
occurrences** in the grid.

- **Recurrence vocabulary.** The four simple frequencies (Daily/Weekly/Monthly/Yearly) with an
  **interval** ("every N"), weekly **by-weekday** (BYDAY, plain weekdays), and an **end**
  (never / until a date / after N). A structured `Recurrence` record + a hand-written
  `RecurrenceRule.TryParse`/`Format` (Application, no Ical.Net) — the Api and composer share it.
- **Rules beyond the subset** (BYSETPOS, BYMONTHDAY, ordinal BYDAY, …) are **not corrupted**:
  `TryParse` returns false, the resource carries the raw `RecurrenceRule` string with
  `recurrenceSupported=false`, the editor shows it **read-only** ("Custom rule"), and the client
  echoes the raw rule back on save so it's **preserved verbatim** (the composer emits it as-is).
- **Editor.** A shared modal (create + edit) with Summary, All-day, Start, **End**, **Location**
  (both were previously unsettable), Attendees, and the Repeat controls. Editing a clicked grid
  item always fetches and edits the **series master** (a grid item may be an expanded occurrence).
  Update uses `If-Match: *` (edits the current version, like split/restore).
- **Grid expansion.** `GET …/events?fromUtc&toUtc&expand=true` returns **one item per occurrence**
  (recurring masters recurrence-expanded via Ical.Net `GetOccurrences`, non-recurring events once),
  each carrying the **master id** so clicking opens the series. Display-only. The **list** view
  still shows one row per series (`↻`); only the grid expands, fetching its visible window.

### Schema

One indexed column **`RecurrenceRule`** (`varchar(1024)`, nullable) on the `CollectionObject` TPH
table, extracted from the VEVENT `RRULE` line on every write — exactly mirroring `Location`
(ADR 0038), so reads surface the rule without parsing blobs (ADR 0004). No new table, no index,
no changed constraints. Migrations regenerated for both providers.

## Consequences

- Users can set/change how an event repeats, and set its end time and location, from the web UI;
  recurring events now appear on every date in the grid.
- The list view keeps master-only rows (a "what exists" list); expansion is grid-only, so a bulk
  list stays one-row-per-series.
- Occurrence expansion reads the blob for recurring candidates — the same on-demand-query exception
  to ADR 0004 already used by `calendar-query` (ADR 0043); sync/listings stay indexed-only.

## Deferred

- Per-instance edits ("this occurrence only" via EXDATE / RECURRENCE-ID overrides) — v1 edits the
  whole series. (The existing Split, ADR 0027, loosely covers "change from here on".)
- Monthly "Nth weekday" / by-month-day in the structured editor (those rules round-trip as custom).
- Client-side or hourly day-grid expansion; drag-to-move.
