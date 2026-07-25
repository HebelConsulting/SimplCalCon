# ADR 0054 — DAV depth: partial calendar/address-data, param-filter, expand

## Status

Accepted — implemented. Closes the ADR 0022/0043 "full-object only" gaps.

## Context

Three DAV REPORT capabilities were unimplemented (returned the full object / a superset):
`calendar-data`/`address-data` **partial retrieval** (a `<comp>`/`<prop>` subset), the query
**`param-filter`** (matching a property's parameters), and `calendar-data` **`expand`**
(response-side recurrence expansion). Clients use partial retrieval to fetch lightweight lists
(bandwidth), param-filter for e.g. "attendees needing action", and expand for a flattened
time-range view.

## Decision

- **Partial retrieval.** `DavDataRequest` (Api) parses the requested `calendar-data`/`address-data`
  tree into provider-agnostic `CalendarDataRequest`/`AddressDataRequest` (per-component prop sets +
  allprop/allcomp, RFC 4791 §9.6 / RFC 6352 §10.4). `IDavDataFormatter` (Infrastructure,
  `DavDataFormatter`) reduces the blob **at the line level** (preserving folding + unknown/X-props),
  dropping unlisted components and properties. VCALENDAR/VTIMEZONE/VCARD are always kept (valid,
  timezone-resolvable output) and UID/RECURRENCE-ID/VERSION are always kept (identifiable/valid).
  Applied in the calendar/addressbook **multiget + query** handlers; a full request returns the
  blob unchanged (no formatting cost).
- **param-filter.** `DavPropFilter` gains `Params` (`DavParamFilter` = name + is-not-defined +
  text-match); `DavFilterParser` parses `param-filter` inside a `prop-filter`, and
  `DavFilterEvaluator` reads the property line's parameters (quote-aware split) and requires **some
  occurrence** to satisfy the value text-match **and** every param-filter. The no-param path keeps
  its original (collection-level negate) behaviour, so existing filters are unaffected.
- **expand.** `CalendarObjectParser.ExpandForData` (Ical.Net) expands recurring components into one
  VEVENT per occurrence in the window — each with a `RECURRENCE-ID` + concrete `DTSTART`/`DTEND`,
  its `RRULE`/`EXDATE` removed (overrides are honored via `Occurrence.Source`). Runs before the
  prop subset, so `expand` + `<comp>`/`<prop>` compose.

All three are **request-time transforms over the blob** — no schema change, no new dependency.

## Consequences

- Clients get exactly the components/properties they ask for (smaller responses), can filter on
  property parameters, and can request a flattened recurrence view — the last also fixes the
  ADR 0022 "no response-side recurrence expansion" gap.
- Query/multiget stay on the ADR-0004 on-demand-query exception (they already read blobs); sync and
  PROPFIND listings are unchanged.

## Simplifications / deferred

- Line-level subsetting always keeps VCALENDAR/VTIMEZONE/VCARD wrappers and UID/RECURRENCE-ID/VERSION
  even if not listed (correctness over strict minimalism); deep per-component `allcomp` nesting is
  approximated via a flat component map.
- `expand` keys occurrences on start and emits UTC times; `limit-recurrence-set` /
  `limit-freebusy-set` and partial retrieval on `sync-collection` are not implemented.
