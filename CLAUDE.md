# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**SimplCalCon** is a multi-tenant server (Clean Architecture, .NET) for storing and synchronizing **calendar entries (events and tasks)** and **contacts** across devices (smartphones, tablets, computers) via **CalDAV/CardDAV**, plus a **REST API** and a **Blazor WASM web UI** for editing, viewing, sharing, and backup.

The specification lives in `docs/spec.md`; every architectural decision is an ADR under `docs/adr/` (index: `docs/adr/README.md`). **No code exists yet** — the repo currently holds the spec and seed ADRs 0001–0015. Update this file as real structure lands; do not let it go stale.

## Working with the user

Before writing any code or making any architectural/design decision, stop and interview the user first: ask questions, lay out the realistic options with their tradeoffs, and let them choose. Do not default to picking an approach and proceeding — decisions here are made collaboratively, one topic at a time.

Before making any automated database schema change (new/modified entities, EF Core configuration, migrations), show the user the proposed new schema and what it modifies — new tables/columns, changed constraints/indexes, affected existing tables — and get their go-ahead before generating the migration and applying it.

## Architecture Decision Records

All design-phase architectural decisions are recorded as ADRs under `docs/adr/` — see `docs/adr/README.md` for the index. Consult the relevant ADR before touching an area it covers; add a new ADR (next sequential number) rather than editing an old one when a decision changes.

- New architectural/design decisions get their own new ADR rather than being written directly into CLAUDE.md — this file stays lean: what's needed to operate in the repo, plus pointers into the ADRs for rationale.
- Whenever a PR implements a piece of already-planned architecture, that **same PR must update the corresponding documentation** (the relevant ADR, `docs/spec.md`, and/or a CLAUDE.md section) to reflect what was actually built — not left as a follow-up task.

Seed decisions at a glance: .NET + EF Core with **PostgreSQL and SQLite both as configurable production databases** (ADR 0001 — everything must work on both providers); dual protocol surface `/dav` + `/api` (0002); DAV protocol layer implemented in-house (0003); hybrid blob-plus-indexed-fields storage (0004); OIDC + per-device app passwords (0005); multi-tenancy with platform/tenant admins (0006); full ACL sharing (0007); events + tasks with internal-first scheduling (0008); REST conventions = house suite minus XML (0009); Blazor WASM UI (0010); trash + version history (0011); sync-token/CTag + WebDAV-Push + SignalR (0012); import/export/takeout/migration (0013); medium scale target (0014); inherited conventions (0015).

## Code style

Prefer C# string interpolation (`$"{a}-{b}"`) over `+`-based string concatenation wherever practical, for readability.

Prefer switch expressions (`x switch { ... }`) over traditional `switch`/`case` statements wherever practical.

**Treat compiler warnings as errors.** A change is not done while it introduces (or leaves) a build warning — fix the underlying cause rather than suppressing it. Enforce via `Directory.Build.props` with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` repo-wide (every project incl. tests). NuGet package-audit warnings (`NU1901`–`NU1904`) are excluded via `<WarningsNotAsErrors>` — a dedicated CI vulnerable-dependency scan owns vulnerability-blocking, so a freshly-published advisory can't break every compile mid-task. (Watch for the XML-comment `--` trap in that props file: a literal double-hyphen — e.g. writing out a `--flag` — makes the file fail to load with a misleading "Invalid framework identifier ''" restore error.)

**Use specific exception types, not bare `ApiException` with a string code.** Throw a dedicated, intent-named subclass that encapsulates code/status/message. Exceptions form a **two-level hierarchy**: a per-area **base** class inherits from `ApiException`, and each concrete error inherits from that area base, so a caller can `catch` a whole area or a single error. One file per class, `Exception` suffix, under an `Errors/Exceptions/<Area>/` folder in the Api project. The global handler translates any `ApiException` into the RFC 7807 response. Cross-cutting codes shared by several endpoints get one exception with static factories. Add a `NoBareApiExceptionTests`-style unit test early that scans `src/` and fails the build if a bare `new ApiException(...)` appears.

**Prefer specific, intent-named exceptions over bare generic ones everywhere — not just `ApiException`.** For a real error *condition* in domain/application logic, throw a dedicated, intent-named subclass rather than a bare `Exception`/`InvalidOperationException`/`ArgumentException` with an ad-hoc string; give a family a small shared base where a caller might `catch` the group. **Reserve bare framework exceptions for genuinely generic, defensive, or unreachable cases**: argument guards (`ArgumentNullException.ThrowIfNull(x)` etc.) and "this should never happen" assertions.

## API architecture (ADR 0009)

The `/api` REST surface follows: HATEOAS hypermedia envelopes (`GET /api` is the discovery document), RFC 7807 problem details written with `contentType: "application/problem+json"`, media-type (Accept-header) versioning, ETag/`If-Match` on every mutation (412 `ETAG_MISMATCH`, 428 `IF_MATCH_REQUIRED`) backed by explicit `Guid` concurrency-token columns (regenerated on every save, portable across PostgreSQL/SQLite — never a DB system column), and **JSON only** (no XML formatters — the DAV surface is the XML protocol here; DTOs may be records).

- **RESTful naming over RPC**: prefer nouns/sub-resources; a `POST` action sub-resource only for genuine state transitions — flag and justify any new verb-in-path route.
- **All resource/diagnostics/root controllers route under a literal `api/` prefix** (explicit per controller, greppable). Protocol/infra endpoints stay at root: `/dav` + `/.well-known/caldav`+`/.well-known/carddav` (DAV, ADR 0002/0003), `/connect/*` + `/.well-known/openid-configuration` (OpenIddict), `/health/live` + `/health/ready`, `/openapi/v1.json` (all environments, anonymous) + `/scalar` (Development only). `GET /` falls through to the Blazor SPA.
- **Every `GET` action gets a companion `HEAD` action** (ASP.NET Core doesn't derive it automatically).

The **`/dav` surface is exempt from the REST conventions** — it follows the WebDAV/CalDAV/CardDAV RFCs (its own XML, ETag, and error semantics; see ADR 0003 for the supported-feature list).

## Persistence (EF Core)

PostgreSQL **and** SQLite are both production providers, configurable per deployment (ADR 0001): every schema, index, migration, and query must work on both — migrations are maintained per provider, CI tests run against both. Blob + extracted-field hybrid storage per ADR 0004: one application-layer write path keeps them in sync; DAV listings/sync/queries must never parse blobs at request time and must structurally exclude trashed objects (ADR 0011) and enforce tenant scoping (ADR 0006).

## Client (Blazor WASM)

Blazor WebAssembly app (ADR 0010), served by the Api host, consuming `/api` with OIDC (code + PKCE), live updates via SignalR (ADR 0012). No conventions yet — establish layout/test guards (bUnit-style) as the UI takes shape and record them here.

## Licensing constraint

The project is licensed Apache 2.0. Only use open-source libraries/dependencies whose license is compatible with Apache 2.0 (e.g. MIT, BSD, Apache-2.0). Never add closed-source, proprietary, or non-OSS-licensed libraries/packages — check the license of any new NuGet/npm/etc. dependency before adding it. Enforce in CI: run `nuget-license` over the whole transitive tree and **fail the build** on any license outside the allowlist in `build/licenses/allowed-licenses.json`; overrides are version-pinned so a Dependabot bump of an overridden package fails the gate until its version is updated.

## Docker & Kubernetes

To be built following the sibling-project pattern (ADR 0015): multi-stage Alpine .NET Dockerfile (SDK build stage, `aspnet:*-alpine` runtime, the base image's built-in non-root `app` user — don't `addgroup`/`adduser`), `HEALTHCHECK` on `/health/ready` for plain `docker run`; Kubernetes uses its own probes against `/health/live` + `/health/ready`. One shared `docker-compose.yaml` (Api + `postgres:*-alpine`) with `restart: unless-stopped`, portable values only — the same file must run unmodified under both `docker compose` and `podman compose`; if a change ever breaks Podman, fix it with a portable value in the shared file rather than adding a Podman-only file. (Podman reboot note: enable `podman-restart.service` once per machine for containers to start after a VM reboot.)
