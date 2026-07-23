# ADR 0019 — REST plumbing as built; media-type versioning deferred

## Status
Accepted (2026-07-23, Phase 1 implementation). **Supersedes the media-type-versioning
commitment of [ADR 0009](0009-rest-api-conventions.md).**

## Context
ADR 0009 committed the `/api` surface to a convention suite. This unit built the first
slice of it and, in doing so, revisited one commitment (versioning) and recorded a
couple of cross-cutting decisions.

## Decision

**Built now (house style, ADR 0009):**
- **Hypermedia envelope** — a `HypermediaResource` base carrying a `links` array of
  `{ rel, href, method }`, a `CollectionResource<T>` for lists, and a discovery
  document at **`GET /api`** (public). Not strict HAL; simple and greppable.
- **RFC 7807 Problem Details** — a global `IExceptionHandler`
  (`ProblemDetailsExceptionHandler`) writes `application/problem+json` via
  `IProblemDetailsService`, carrying a stable `errorCode` extension. In **Development
  only**, unexpected 500s include an `exception` extension for debuggability;
  production stays generic.
- **Typed exception hierarchy** — an **abstract** `ApiException` base (so a bare
  `new ApiException(...)` cannot compile) with per-area bases and concrete,
  intent-named subclasses under `src/SimplCalCon.Api/Errors/Exceptions/<Area>/`. A
  `NoBareApiExceptionTests` guard scans `src/` and fails the build if the pattern
  reappears.
- **ETag / If-Match** — a global `ETagResultFilter` stamps the `ETag` from an
  `IETaggedResource`'s concurrency token on 2xx responses; a `[RequireIfMatch]`
  action filter enforces the precondition on mutations (**428** if missing, **412**
  if stale/malformed) and stashes the parsed token; the write path sets it as the
  EF `OriginalValue` so a stale token becomes `DbUpdateConcurrencyException` → **412
  `ETAG_MISMATCH`**.
- **OpenAPI + Scalar** — `GET /openapi/v1.json` (anonymous, all environments) and the
  Scalar UI at `/scalar` (Development only).
- **First resources** — `GET /api/me` (bearer) and full `GET/HEAD/POST/DELETE
  /api/app-passwords` (create returns the secret once, with an `ETag`; DELETE revokes
  under If-Match). The old `/api/whoami` placeholder is removed; `/connect/userinfo`
  (OIDC) and `/dav/whoami` (DAV) remain.

**Deferred — media-type versioning.** ADR 0009 committed to media-type (Accept-header)
versioning from the start. We **ship v1 implicitly** (plain `application/json`) and
add media-type negotiation only when a v2 actually exists. Rationale: there is exactly
one consumer (our own Blazor client) and no second version, so the negotiation
machinery is unused ceremony today; nothing in the envelope/handler design precludes
adding a media-type version reader later. This ADR supersedes that one clause of
ADR 0009; the rest of ADR 0009 stands.

## Consequences
- **Cross-provider gotcha recorded:** SQLite cannot `ORDER BY` a `DateTimeOffset`
  (Postgres can). The app-password list orders client-side; broader
  `DateTimeOffset` ordering (calendar time-range queries) will need a sortable stored
  representation — flagged for the collections/object-store unit.
- **Namespace gotcha:** the new `SimplCalCon.Api.Errors` namespace shadows OpenIddict's
  static-imported `Errors` constants; fully-qualify `OpenIddictConstants.Errors` at
  those call sites.
- The thin auth endpoints from ADR 0018 are now partly superseded by real resources
  (`/api/me`); the OIDC/DAV protocol endpoints are unchanged.
- When versioning is eventually added, it lands as its own ADR reactivating ADR 0009's
  approach, plus the `VersionedContentType` handling the sibling project used.
