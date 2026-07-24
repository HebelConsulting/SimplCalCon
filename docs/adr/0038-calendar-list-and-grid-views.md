# 0038 — Calendar list + grid views (with an extracted LOCATION field)

## Status

Accepted (2026-07-24).

## Context

The Calendar tab (ADR 0025) was an agenda list grouped by day, with inline "new event" and
"import" forms. ADR 0025 explicitly deferred a month/week grid and richer views. Users want the
same kind of sortable/filterable/**resizable** table we built for Contacts (ADR 0036), plus a
proper calendar grid to switch to.

Showing a **Location** column is the one part that isn't purely client-side: calendar objects
extracted `Summary`, start/end, all-day and recurring — but not `LOCATION`. Per ADR 0004, listing
endpoints must not parse blobs at request time, so a Location column needs the field extracted.

## Decision

Two switchable views on the Calendar tab, plus a small backend addition.

- **List view (default)** — a table of the calendar's events with columns **Start · Title ·
  Location · Repeats**. Sortable by any column (default: Start ascending), each column has its own
  text filter, and **Repeats** has a "only" checkbox (recurring-only). Columns are drag-resizable,
  reusing `columnResize.js` (ADR 0036). The table stays in the DOM (hidden via CSS in calendar
  mode) so the resizer keeps its widths across view switches.
- **Calendar view** — a **month**/**week** grid (switcher), navigable with prev/next/today. Days
  are cells with event **chips** (time + title); clicking a chip — or a list row — opens a **detail
  Modal** (When / Location / Attendees, plus the History link and the Split control from ADR 0027).
  A ribbon **List / Calendar** toggle switches views.
- **New event** and **Import** move to ribbon buttons → `Modal` (consistent with Contacts,
  ADR 0036), keeping the content area for the table/grid.
- **One fetch, both views.** The page loads *all* the calendar's events once; the list shows them
  all (sorted/filtered), and the grid filters that in-memory set to the visible period client-side
  — no per-period refetch, no new endpoint. Times are localized client-side (`ToLocalTime`).
- **Backend — extracted `Location`.** A new nullable `Location` column on calendar objects
  (`CalendarObject.Location`, `maxLength 1024`), extracted from the iCal `LOCATION` on every write
  in `ObjectStore` (like `Summary`), and surfaced on `EventResource.Location` / `EventDto.Location`.
  No index (filtering is client-side over the loaded list). Migrations for both providers
  (`AddCalendarObjectLocation`).

## Consequences

- The list gives fast triage (sort/filter/resize); the grid gives an at-a-glance overview — from a
  single events fetch.
- **Recurring events show only at their master start in the grid** — there is no client-side
  recurrence expansion — but the Repeats column/indicator (`↻`) flags them, and the DAV/calendar
  query path still expands server-side (ADR 0022).
- The grid is **display-only** for now: no drag-to-move/resize, no in-grid creation — those go
  through the list/detail. Authoring `LOCATION` in the web new-event form is **deferred** (location
  currently arrives via import or native clients); the column already displays and searches it.
- **Deferred:** an hourly time-grid (single-day view), drag-to-move/resize, in-grid recurrence
  expansion, and location editing in the create form.
