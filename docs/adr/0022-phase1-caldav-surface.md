# ADR 0022 — Phase 1 CalDAV surface

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
With CardDAV shipped (ADR 0021), this unit adds **CalDAV (calendars)**, reusing the
same WebDAV plumbing (the XML framework, custom HTTP-method attributes, `IObjectStore`,
`IDavRepository`, and sync-collection). The one genuinely new problem is time-range
querying, which needs recurrence expansion.

## Decision

**URL layout** mirrors CardDAV: calendar-home-set `/dav/calendars/{userId}/`,
calendars `.../{userId}/{cal}/`, objects `.../{userId}/{cal}/{name}`;
`/.well-known/caldav` → 301 `/dav/`. The **principal resource now advertises both**
`addressbook-home-set` and `calendar-home-set`.

**Operations**: PROPFIND (Depth 0/1); GET/PUT/DELETE with ETag + If-Match/If-None-Match;
REPORT — `calendar-multiget`, `sync-collection`, and **`calendar-query` with
`time-range`**; **MKCALENDAR** (RFC 4791) and extended MKCOL, honoring a requested
`supported-calendar-component-set`; a default `calendar` is auto-provisioned on first
calendar-home-set access.

**Components**: **VEVENT and VTODO** (ADR 0008). `supported-calendar-component-set` is
advertised per calendar from the `SupportsEvents`/`SupportsTasks` flags, and MKCALENDAR
sets those from the requested component set (so Apple can create a separate reminders
calendar).

**Time-range = on-the-fly recurrence expansion** (the chosen approach; no schema
change). `IDavRepository.QueryCalendarObjectsAsync` pre-filters candidates in SQL
(non-recurring by `DtStartUtc`/`DtEndUtc` overlap; all recurring and no-start objects),
then expands recurring candidates precisely with **Ical.Net** (`GetOccurrences` from the
range start, bounded by the range end) — `CalendarOccurrence.OverlapsRange` in
Infrastructure (the Api doesn't reference Ical.Net). The materialized occurrence-window
index (ADR 0004) remains a later optimization.

**Deferred**: free-busy-query and iTIP scheduling — the Phase 2 scheduling unit
(RFC 6638, ADR 0008), which builds on the same expansion.

## Consequences
- **Verification**: integration tests drive real PROPFIND/REPORT/PUT/MKCALENDAR XML,
  including a `calendar-query` time-range that matches a single in-window event and a
  recurring event (via expansion) while excluding an out-of-window event; plus
  sync-collection deltas. The CalDAV rows of `docs/dav-client-matrix.md` track native
  clients.
- **Known gaps** (logged, not silently narrowed): `calendar-query` evaluates only the
  `time-range` filter (comp/prop/text filters aren't applied — it returns the correct
  superset otherwise); `calendar-data` returns the full object (no
  component/property filtering or recurrence expansion in the response); time-range
  expansion keys on occurrence **start**, so an event that begins before the window but
  spans into it can be missed (rare; a documented edge case).
- **Gotcha**: `Calendar` collides with `System.Globalization.Calendar` in the
  time-range controller — aliased.
- DAV `PUT`/`DELETE` reuse `IObjectStore`, so calendar writes get revisions,
  tombstones, and the change-sequence bump for free — identical to CardDAV.
