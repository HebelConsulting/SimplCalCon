# ADR 0002 — Dual protocol surface: CalDAV/CardDAV for devices, REST for our own clients

## Status
Accepted (2026-07-23, spec interview)

## Context
The core promise is synchronization with stock device apps (iOS/macOS Calendar &
Contacts, DAVx⁵ on Android, Thunderbird, Outlook). Those apps only speak
CalDAV/CardDAV. Our own web UI, admin tooling, and backup features are better served
by a modern JSON REST API following the house conventions.

## Decision
Two protocol surfaces over one data store:

- **CalDAV (RFC 4791) + CardDAV (RFC 6352) over WebDAV (RFC 4918)** as the
  device-facing sync surface, routed under `/dav` (with `/.well-known/caldav` and
  `/.well-known/carddav` redirects for autodiscovery).
- **REST API under `/api`** (ADR 0009) as the surface for the Blazor web UI,
  administration, import/export/takeout, sharing management, trash/version-history
  restore, and anything else our own clients need.

Both surfaces read and write the same collections/objects; ETags are identical
across surfaces (one revision → one ETag), so a DAV client and the web UI get
coherent optimistic-concurrency behavior.

## Consequences
- Two request pipelines to secure and test; auth differs per surface (ADR 0005).
- Features must state which surface(s) expose them (e.g. sharing is managed via
  REST/web UI only; DAV reports privileges read-only — ADR 0007).
- The DAV surface constrains the storage model: lossless round-tripping of client
  payloads is mandatory (ADR 0004).
