# ADR 0073 — Deep comp nesting in partial calendar-data

## Status

Accepted — implemented. Closes the last ADR 0054 deferral ("deep allcomp nesting is approximated").

## Context

Partial `calendar-data` (ADR 0054) modelled the requested `<comp>` structure as a **flat map keyed by
component name**. That collapses the RFC 4791 comp **tree**: it can't represent the same component
name appearing under two parents with different selections (e.g. `VALARM` under `VEVENT` trimmed to
`ACTION`, but under `VTODO` kept whole), and it couldn't scope `allcomp`/`allprop` precisely per level.
Common linear requests (VCALENDAR→VEVENT→VALARM) worked; divergent nesting was approximated.

## Decision

Model the request as a proper tree and walk the blob against it.

- **`DavCompSelection`** gains `Comps` — the requested child components keyed by name, each its own
  selection node (`AllProps`, `AllComps`, `Props`, `Comps`). `CalendarDataRequest` now holds a single
  `Root` (the VCALENDAR comp) instead of a flat dictionary.
- **`DavDataRequest.ParseComp`** recurses `<comp>` into the tree (a childless `<comp>` still means "all
  props + all sub-components", per RFC).
- **`DavDataFormatter.Subset`** walks the object with a stack of selection nodes: the top component
  (VCALENDAR/VCARD) is governed by `Root`; a child is kept iff the parent node lists it (→ its own
  node) or has `allcomp` / is always-kept (VTIMEZONE) (→ a "keep-all" node); properties are kept by
  `allprop` / always-keep (UID/RECURRENCE-ID/VERSION) / explicit `prop`. Depth and per-parent
  divergence are now exact. `address-data` reuses the same walk with a synthetic VCARD root.

Applies everywhere the formatter runs — multiget, calendar-query/addressbook-query, and
`sync-collection` (ADR 0070).

## Consequences

- Nested and divergent comp requests are honored exactly, not approximated; the whole ADR 0054 depth
  surface (subset · param-filter · expand · limit-recurrence-set · partial-on-sync · **deep nesting**)
  is now complete.
- Slightly more parsing/state (a tree + a per-frame selection), but the line-level walk is unchanged
  in spirit.

## Deferred

- None outstanding for `calendar-data` shaping.
