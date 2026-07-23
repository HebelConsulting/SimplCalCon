# ADR 0017 — EF Core persistence & dual-provider migration layout

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
ADR 0001 commits to EF Core with **both** PostgreSQL and SQLite as configurable
production providers, and states "migrations are maintained per provider." EF Core
keeps exactly one model snapshot per `DbContext` per migrations assembly, so two
providers cannot share one migrations assembly. This ADR records the resulting
project layout and the DbContext conventions established while building it.

## Decision

**Project layout.**
- `SimplCalCon.Infrastructure` holds the single `SimplCalConDbContext`, the
  `IEntityTypeConfiguration<T>` classes, and references only the **provider-agnostic**
  `Microsoft.EntityFrameworkCore.Relational` package.
- `SimplCalCon.Infrastructure.Sqlite` and `SimplCalCon.Infrastructure.Postgres` are
  thin projects that each reference Infrastructure, pull in their one provider
  package, and own that provider's `Migrations/` folder plus an
  `IDesignTimeDbContextFactory<SimplCalConDbContext>` (design-time connection strings
  are placeholders — generating migrations needs a configured provider, not a live
  database).

**Tooling.** `dotnet-ef` is pinned in a local tool manifest (`.config/dotnet-tools.json`)
at the EF Core version. Add a migration per provider, e.g.:
`dotnet ef migrations add <Name> -p src/SimplCalCon.Infrastructure.Sqlite -s src/SimplCalCon.Infrastructure.Sqlite -o Migrations`
(and the same for `.Postgres`). **Both provider migrations must be regenerated in the
same change** whenever the model changes.

**Packages** are declared under Central Package Management (ADR 0009's
`Directory.Packages.props`): EF Core `10.0.10`, Npgsql provider `10.0.3`.

**Conventions established.**
- **Concurrency token / ETag** (ADR 0009): every `IHasConcurrencyToken` entity has
  its `ConcurrencyToken` configured as an EF concurrency token in `OnModelCreating`
  and **regenerated to a fresh `Guid` on every insert/update** in `SaveChanges` —
  never set by callers.
- **DbContext invariants throw `InvalidOperationException`** (the sibling project's
  deliberate exception to the specific-exception rule, per CLAUDE.md): currently the
  **group-membership cycle check**, which the Api boundary will translate into a
  specific `ApiException`.
- **Enums are stored as strings** (`HasConversion<string>`, `maxLength 20`) for
  readability and resilience to reordering, on both providers.
- **Case-insensitive uniqueness uses normalized shadow columns**
  (`NormalizedEmail`, `NormalizedName`), never a provider collation (ADR 0001).
- **Cascade rules**: deleting a `User`/`Principal` cascades to its `AppPasswords`,
  `Tokens`, and outgoing `GroupMemberships`; the membership `MemberId` FK and the
  `Principal → Tenant` FK are `Restrict` (block accidental mass deletes; a second
  cascade path into `Principals` is avoided).

**Known carve-out.** The SQLite provider transitively pulls
`SQLitePCLRaw.lib.e_sqlite3 2.1.11`, which carries advisory `NU1903`
(GHSA-2m69-gcr7-jv3q). The only fix is a **major** 3.x jump EF Core 10.0.10 was not
built against, so it is **not** force-pinned. It is harmless to the build (`NU1901`–
`NU1904` are carved out of warnings-as-errors, ADR 0015) and is owned by the CI
vulnerability scan; revisit when EF Core ships a provider referencing a patched 2.1.x.

## Consequences
- One model, two migration histories kept in lockstep — a model change is not done
  until both provider migrations are regenerated and both build.
- The DbContext is provider-agnostic; provider selection happens at composition
  (design-time factories now; the Api host's DI at runtime, added with the auth
  wiring).
- Applying migrations to PostgreSQL needs a running server (the local
  `docker-compose.yaml`); SQLite applies with no server, which keeps it the fast
  test-parity path.
