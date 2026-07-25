# ADR 0068 — calendar-data limit-recurrence-set

## Status

Accepted — implemented. Completes an ADR 0054 deferred item.

## Context

CalDAV `calendar-data` supports two ways to bound a recurring component in a REPORT: `expand`
(flatten to one VEVENT per occurrence — built in ADR 0054) and **`limit-recurrence-set`** (RFC 4791
§9.6.5 — keep the recurring **master** but return only the overridden instances that overlap a
window). Apple Calendar and some other clients send `limit-recurrence-set` to avoid pulling every
historical override; we returned the full object (a benign superset), which was listed as deferred.

## Decision

Honor `<C:limit-recurrence-set start=".." end=".."/>` inside `calendar-data` on multiget and
calendar-query.

- **Parse:** `DavDataRequest` reads the element into a new `RecurrenceLimit(StartUtc, EndUtc)` on
  `CalendarDataRequest` (`DavNames.CalLimitRecurrenceSet`).
- **Apply:** `CalendarObjectParser.LimitRecurrenceSet` keeps the master component(s) and only the
  overridden instances (components with a `RECURRENCE-ID`) whose own moved time overlaps the window
  (`s < end && e >= start`); the `RRULE` is left intact (unlike `expand`).
- **Precedence:** `expand` and `limit-recurrence-set` are alternatives; if both appear, `expand` wins
  (it already bounds the range).

**Gotcha (why rebuild, not remove):** a recurring master and all its overrides share one `UID`, and
Ical.Net's component collections match `Remove` by `UID` — so removing an out-of-range override drops
the *master*. `LimitRecurrenceSet` therefore **rebuilds** a fresh calendar with the kept components
(master + in-range overrides + VTIMEZONEs) rather than removing from the loaded one.

## Consequences

- A client asking for `limit-recurrence-set` gets the compact, spec-conformant result (master + the
  relevant overrides) instead of the whole history.
- No schema or write-path change; it's a read-time `calendar-data` transform alongside the existing
  subset + `expand`.

## Deferred

- Partial `calendar-data`/`address-data` on `sync-collection` (still returns full blobs there).
