# ADR 0009 — REST API conventions: house suite, JSON-only (no XML negotiation)

## Status
Accepted (2026-07-23, spec interview)

## Context
The sibling project (SimplArchive) established a REST convention suite: HATEOAS
hypermedia envelopes, RFC 7807 problem details with a typed two-level exception
hierarchy, media-type (Accept-header) versioning, ETag/`If-Match` on every mutation,
and JSON+XML dual content negotiation. The XML negotiation forced every DTO into
XmlSerializer-compatible mutable classes and has no consumer here — the DAV surface
*is* the XML protocol for third parties.

## Decision
The `/api` surface adopts the house suite **minus XML**:

- **HATEOAS** hypermedia envelopes with `links`; `GET /api` is the discovery
  document; RESTful resource naming over RPC verb routes (verb sub-resources only
  for genuine state transitions, justified case by case).
- **RFC 7807** problem details written with `application/problem+json`; application
  errors are intent-named `ApiException` subclasses in a two-level per-area
  hierarchy (see CLAUDE.md), guarded by a no-bare-`ApiException` unit test.
- **Media-type versioning** via vendor media types on `Accept`.
- **ETag/`If-Match`** on every mutation (412 `ETAG_MISMATCH`, 428
  `IF_MATCH_REQUIRED`); object ETags are the same revision ETags the DAV surface
  serves (ADR 0002), backed by explicit concurrency-token columns (ADR 0001).
- **JSON only** — no `AddXmlSerializerFormatters()`; DTOs may be records with
  `init` setters.
- Every `GET` action has a companion `HEAD` action (explicit, per house convention).
- **OpenAPI** via `Microsoft.AspNetCore.OpenApi` at `/openapi/v1.json` (anonymous,
  all environments); **Scalar** UI at `/scalar` in Development only.
- All controllers route under the literal `api/` prefix; protocol/infra endpoints
  stay at root: `/dav`, `/.well-known/*`, `/connect/*` (OIDC), `/health/*`,
  `/openapi`, `/scalar`; `GET /` falls through to the Blazor SPA.

## Consequences
- One convention delta from the sibling project (XML) — documented here so tooling
  or docs copied over don't reintroduce it.
- The DAV surface is exempt from all of this (it follows its RFCs, including its
  own ETag and XML rules); only `/api` carries the suite.
