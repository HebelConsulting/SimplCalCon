# ADR 0001 — Technology stack: .NET, Clean Architecture, EF Core on PostgreSQL *and* SQLite

## Status
Accepted (2026-07-23, spec interview)

## Context
SimplCalCon needs a server stack for a multi-tenant CalDAV/CardDAV + REST backend and
a web UI. The sibling project (SimplArchive) established working conventions on
.NET/C#, Clean Architecture, EF Core, and PostgreSQL. Deployments range from small
self-hosted installs to a hosted offering with hundreds of tenants (ADR 0014).

## Decision
- **.NET (current LTS/STS as available, C#)** with a Clean Architecture solution
  layout (Domain / Application / Infrastructure / Api / Client).
- **EF Core** for persistence with **two supported production database engines,
  selected by configuration**: **PostgreSQL** (primary target, hosted/medium
  deployments) and **SQLite** (small self-hosted installs). SQLite doubles as the
  test-parity engine.
- **Blazor WASM** for the web client (ADR 0010), served by the Api host.

## Consequences
- Every migration, index, and query must work on **both** providers — no
  Postgres-only features (no `xmin`, no jsonb-dependent queries, no ILIKE
  assumptions) unless a provider-neutral fallback exists. Concurrency tokens are
  explicit `Guid` columns, not database system columns.
- Migrations are maintained per provider (EF Core cannot share one migration set
  across providers); CI runs the test suite against both.
- Text search/collation behavior differs between engines; case-insensitive matching
  must be implemented deliberately (normalized shadow columns where needed) rather
  than relying on collations.
