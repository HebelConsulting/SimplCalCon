# ADR 0004 — Hybrid storage: original blob as source of truth + indexed fields

## Status
Accepted (2026-07-23, spec interview)

## Context
DAV clients send full iCalendar/vCard payloads and expect byte-level-faithful
round-trips of properties the server doesn't understand (X- extensions, exotic
recurrence, client-specific fields). Meanwhile the REST API, web UI, sync queries,
and scheduling need structured, indexed access. A fully normalized model is lossy;
raw blobs alone make every query a parse.

## Decision
Each object (event series, task, contact) is stored as:

1. **The original blob** (`.ics` component / `.vcf` card) — the source of truth,
   returned verbatim to DAV clients, preserved across edits of unrelated fields.
2. **Extracted indexed fields**, maintained transactionally on every write:
   - common: UID, ETag/revision id, collection, tenant, deleted/trash state;
   - events/tasks: summary, dtstart/dtend (UTC + original TZID), recurrence flag,
     an **expanded occurrence window index** (materialized occurrences over a
     rolling horizon, e.g. ±2 years, refreshed on write and by background roll-over)
     for time-range queries and free-busy;
   - contacts: display/given/family name, normalized emails, phones, org.

Writes from either surface (DAV `PUT` or REST/web UI edit) go through **one
application-layer write path**: parse/validate → update blob → re-extract fields →
new revision + ETag (ADR 0011 keeps prior revisions).

Web-UI edits are applied by **patching the parsed blob** (modify only the properties
the edit touches, re-serialize), never by regenerating the object from the indexed
fields — this preserves unknown properties.

## Consequences
- Lossless DAV round-trips and fast indexed queries; the cost is the invariant that
  blob and extracted fields never diverge — enforced by the single write path and a
  consistency check in tests.
- Time-range queries and sync never parse blobs at request time (spec §5 guardrail).
- The occurrence-window horizon is a documented limitation: queries beyond the
  horizon fall back to on-the-fly expansion of the (few) matching series.
