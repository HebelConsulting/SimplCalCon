# ADR 0024 — Deployment & CI foundation

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
The sync core, sharing, and both DAV surfaces work, but nothing was packaged or
guarded by CI. This unit makes SimplCalCon runnable, shippable, and regression-tested,
per the deployment/quality conventions (ADR 0015).

## Decision

**Health endpoints.** `AddHealthChecks()` with `/health/live` (liveness — process up,
no checks) and `/health/ready` (readiness — a `DatabaseHealthCheck` running
`Database.CanConnectAsync`, tagged `ready`). Both anonymous. Orchestrators and the
Docker `HEALTHCHECK` target these; the serving port is **9080** (ADR, Kestrel in
`appsettings.json`).

**Docker.** A multi-stage Alpine Dockerfile (`sdk:10.0-alpine` build →
`aspnet:10.0-alpine` runtime), running as the image's built-in non-root `app` user,
`EXPOSE 9080`, `HEALTHCHECK` on `/health/ready`. Verified: the image builds and a
container starts, serves `/health/{live,ready}` and `GET /api`, and runs the bootstrap
seeder.

**Compose.** One `docker-compose.yaml` (Api + `postgres:16-alpine`, `restart:
unless-stopped`, `depends_on` health condition) that runs unmodified under **both**
`docker compose` and `podman compose`. It runs the Api in **Development** (ephemeral
OIDC keys, Scalar, demo seeding) for a zero-config demo.

**Helm.** A chart at `deploy/helm/simplcalcon` — Deployment with startup/liveness/
readiness probes and a non-root `securityContext`, Service on 9080, a Secret for the DB
connection + platform-admin password, an optional Ingress, and an OIDC-certificate
secret-mount hook (Production requires signing + encryption certs — the app fails fast
without them). `helm lint` clean.

**CI** (`.github/workflows/ci.yml`), three jobs:
- **test-sqlite** — build (warnings-as-errors) + full suite on SQLite.
- **test-postgres** — the same suite against a `postgres:16-alpine` service; the
  integration-test host reads `SIMPLCALCON_TEST_DB_PROVIDER`/`_CONNECTION` (default
  SQLite) so it can target PostgreSQL. Honors "tests run against both" (ADR 0001).
- **license-gate** — `nuget-license` over the deployable Api project's transitive tree
  against `build/licenses/allowed-licenses.json` (MIT/Apache-2.0/BSD/PostgreSQL/MS-PL/
  0BSD), failing on anything else (ADR 0015).

## Consequences
- The license gate scans the **production** project (Api), not test-only packages; the
  allowlist may need a tweak on the first CI run if a transitive dependency reports a
  license URL rather than an SPDX expression.
- The PostgreSQL CI leg shares one database across the (serialized) test classes; tests
  stay isolated via fresh per-test users, unique resource names, and idempotent seeds.
- `.NET 10` is provisioned on the runners via `actions/setup-dotnet@v4` (`10.0.x`).
- The Blazor client isn't containerized yet (the Api doesn't serve it); that lands with
  the web-UI unit.
