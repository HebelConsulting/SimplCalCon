# 0043 — DAV query-filter evaluation (calendar-query / addressbook-query)

## Status

Accepted (2026-07-25).

## Context

The CalDAV `calendar-query` REPORT evaluated **only** a `time-range` (ADR 0022 known gap), and the
CardDAV `addressbook-query` REPORT ignored its filter entirely and returned the whole collection
(ADR 0021 known gap). Native clients use these filters for server-side search, so the superset
behaviour is both incorrect and needlessly heavy.

The tension is ADR 0004: "DAV queries must never parse blobs at request time." But evaluating a
`prop-filter`/`text-match` on an arbitrary property (`NICKNAME`, `DESCRIPTION`, …) needs the blob —
the indexed columns cover only a few fields. Note the calendar path **already parses** (Ical.Net,
for recurrence expansion), so the *on-demand query REPORTs* parsing is not new ground.

## Decision

- **In-house model** (`Application.Abstractions.Storage`): `ContactQueryFilter` /
  `CalendarQueryFilter`, `DavPropFilter`, `DavTextMatch`, `FilterTest`, `TextMatchType`.
- **Parse in the Api** (`Api/Dav/Xml/DavFilterParser`) — XML → model; the Api stays free of blob
  parsing.
- **Evaluate in Infrastructure** (`DavFilterEvaluator`) by reading properties from the object blob
  at the **line level** (RFC 5545/6350 unfolding), so **any named property** is filterable. This is
  a deliberate, documented exception to ADR 0004's no-parse rule **scoped to the on-demand query
  REPORTs only** — the frequent sync/PROPFIND listing paths stay indexed-only.
- **Repository:** `QueryCalendarObjectsAsync(filter)` and `QueryContactObjectsAsync(filter)`
  SQL-prefilter (collection, not-trashed, component type, time-range) then evaluate the parsed
  filter per candidate.

### v1 scope

- **calendar-query:** `comp-filter` component (VEVENT/VTODO, matched on the indexed `ComponentType`)
  + `time-range` (as before) + `prop-filter` with `text-match` (always substring, per RFC 4791) and
  `is-not-defined`; prop-filters combine **allof** (RFC 4791 comp-filter semantics).
- **addressbook-query:** `prop-filter` with `text-match` — **match-type** `equals` / `contains` /
  `starts-with` / `ends-with` and `negate-condition` (RFC 6352) — and `is-not-defined`; the filter
  `test` is `allof` / `anyof` (default **anyof**).
- All text matches are **case-insensitive** (the default DAV collations).

## Consequences

- Native clients get correct server-side search on both surfaces; results shrink to real matches.
- **Deferred:** `param-filter`; `comp-filter` nesting beyond `VCALENDAR > VEVENT/VTODO`; per-component
  property scoping (the whole-blob scan can also see VCALENDAR-level properties); explicit collation
  negotiation (only the caseless default is honoured).
