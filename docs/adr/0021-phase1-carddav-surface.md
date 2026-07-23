# ADR 0021 — Phase 1 CardDAV surface

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
ADR 0003 committed to an in-house WebDAV/CalDAV/CardDAV implementation. This unit
builds the first DAV surface — **CardDAV (contacts)** — on top of the object store
(ADR 0020), establishing the reusable WebDAV plumbing that CalDAV will extend.
CardDAV first because it has no time-range/recurrence, so the plumbing lands cleanly.

## Decision

**URL layout — principal-scoped (no tenant in the path, ADR 0006):**
`/dav/principals/{userId}/`, addressbook-home-set `/dav/addressbooks/{userId}/`,
collections `.../{userId}/{book}/`, objects `.../{userId}/{book}/{name}`. Discovery
via `/.well-known/carddav` → 301 `/dav/`.

**Operations (full syncable surface):**
- Discovery: `PROPFIND` on the service root/principal returns `current-user-principal`
  and `addressbook-home-set`.
- `PROPFIND` on home/collection/object (Depth 0 and 1).
- `GET`/`PUT`/`DELETE` with `ETag`, honoring `If-Match` and `If-None-Match: *`
  (412 on precondition failure), mapped to the object concurrency token.
- `REPORT`: `addressbook-multiget`, `addressbook-query`, and **`sync-collection`**
  (RFC 6578).
- `MKCOL` (extended) to create address books; a default **`contacts`** book is
  **auto-provisioned** on first home access.
- `OPTIONS` advertises `DAV: 1, 3, addressbook`.

**Sync-token** is opaque, encoding the collection's `ChangeSequence` (ADR 0020). An
absent token = initial sync; a foreign/malformed token returns the RFC 6578
`valid-sync-token` precondition (403) to force a full resync. Because tombstones are
retained (trash purge deferred), every real token stays serviceable for reporting
removals.

**Privileges** are reported read-only: `current-user-privilege-set` (owner holds the
full set), `supported-report-set`, `supported-address-data` (vCard 3.0/4.0). No ACL /
DAV `ACL` method yet (ADR 0007 unit).

**Auth & isolation:** every `/dav` route uses the DAV Basic app-password scheme
(ADR 0005); a user may only touch their own principal (403 otherwise) — no sharing yet.

**In-house XML framework** (`Api/Dav/Xml`): `DavNames` (namespaced element table),
`DavResource` (a resource's full property set), `MultiStatus` (207 builder that
selects per `PropRequest`, reporting requested-but-absent props as 404), and
`PropRequest` (prop / allprop / propname). Custom HTTP methods are `HttpMethodAttribute`
subclasses (`HttpPropfind`/`HttpReport`/`HttpMkcol`); routing already tolerates
trailing slashes, so each action carries a single template.

**Verification:** integration tests drive real PROPFIND/REPORT/PUT/MKCOL XML and assert
the 207 bodies, ETags, and sync deltas; plus a manual native-client acceptance
checklist (`docs/dav-client-matrix.md`) tracking the ADR 0003 interop promise.

## Consequences
- **CalDAV is the next unit**, reusing this plumbing and adding calendar-query
  time-range (which needs the deferred occurrence index) and iTIP scheduling (Phase 2).
- **`addressbook-query` filters are not evaluated yet** — it returns all live objects
  with the requested props (a correct superset); real filter/limit evaluation is a
  follow-up. Logged here rather than silently narrowing.
- `address-data` always returns the full card (no partial-retrieval / property
  filtering) in v1.
- Object writes reuse `IObjectStore`, so DAV `PUT`/`DELETE` get revisions, tombstones,
  and the change-sequence bump for free.
