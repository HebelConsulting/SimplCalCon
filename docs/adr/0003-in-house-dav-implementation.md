# ADR 0003 — Implement the WebDAV/CalDAV/CardDAV protocol layer in-house

## Status
Accepted (2026-07-23, spec interview)

## Context
The .NET ecosystem has no maintained, production-grade CalDAV/CardDAV *server*
library. Candidates (e.g. NWebDav) cover generic WebDAV at best, are semi-maintained,
and would end up as an owned fork. The project's licensing rule (Apache-2.0-compatible
only, CI-enforced) further narrows the field.

## Decision
Implement the protocol layer ourselves as ASP.NET Core endpoints/middleware:
`PROPFIND`, `PROPPATCH`, `REPORT` (multiget, calendar-query, addressbook-query,
sync-collection, free-busy), `MKCALENDAR`/extended `MKCOL`, `GET`/`PUT`/`DELETE`
with ETag semantics, and the discovery properties (`current-user-principal`, home
sets, `supported-report-set`, …).

Use maintained MIT/Apache libraries **only for the data formats**, not the protocol —
e.g. **Ical.Net** (MIT) for iCalendar parsing/recurrence and a vetted vCard library
(or in-house vCard parsing if none meets the license/maintenance bar).

## Consequences
- Full control over protocol behavior, tenancy integration, and ACL enforcement; no
  dead-dependency risk on the most critical surface.
- The spec must enumerate supported RFC features explicitly; anything unlisted is
  deliberately unsupported. Initial commitment: RFC 4918 (core WebDAV, minus locking),
  RFC 4791, RFC 6352, RFC 6578 (sync-collection), RFC 5397
  (current-user-principal), RFC 6638 (scheduling, Phase 2), WebDAV-Push draft
  (ADR 0012). WebDAV `LOCK`/`UNLOCK` is **not** supported (class 2); we advertise
  DAV class 1 + 3 + the calendar/addressbook capabilities.
- Interoperability is our burden: the client matrix (iOS/macOS, DAVx⁵, Thunderbird)
  is part of the definition of done for DAV changes (spec §5).
- XML handling (DAV property namespaces) needs a small, well-tested in-house
  infrastructure layer — budgeted as its own early implementation task.
