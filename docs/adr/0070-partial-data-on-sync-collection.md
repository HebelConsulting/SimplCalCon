# ADR 0070 — Partial calendar-data / address-data on sync-collection

## Status

Accepted — implemented. Completes the last ADR 0054 deferred wire item.

## Context

ADR 0054 made `calendar-data` / `address-data` honor a requested `<comp>`/`<prop>` subset (and
`expand` / `limit-recurrence-set`, ADR 0068) — but only on **multiget** and **calendar-query** /
**addressbook-query**. A **`sync-collection`** REPORT (RFC 6578) that requested `calendar-data` in its
`<prop>` still got the **full** blob per changed resource. A client using sync as its primary delta
mechanism (DAVx⁵, others) couldn't get trimmed payloads there.

## Decision

Apply the same `IDavDataFormatter` transform in the `sync-collection` handlers, exactly as
multiget/query do:

- `CalDavCollectionController.SyncCollectionAsync` parses the request's `calendar-data`
  (`DavDataRequest.ParseCalendarData`) and emits each changed resource via
  `CalendarObjectResource(href, o, FormatCalendar(o, calendarData))` — subset, `expand`, and
  `limit-recurrence-set` all apply; an empty request still returns the full blob.
- `CardDavCollectionController.SyncCollectionAsync` does the same for `address-data`
  (`FormatContact`). Removed-resource `404` responses and the `sync-token` are unchanged.

No schema, no new surface — a two-line wiring in each sync handler reusing the existing formatter.

## Consequences

- `sync-collection` responses honor the same partial-data controls as multiget/query, so a
  sync-first client gets consistent, trimmed payloads.
- The whole ADR 0054 depth surface (subset / `param-filter` / `expand` / `limit-recurrence-set` /
  partial-on-sync) is now uniform across all read REPORTs.

## Deferred

- Deep `allcomp` nesting (still a flat component map, per ADR 0054).
