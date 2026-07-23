# ADR 0016 — Phase 1 identity & tenancy data model

## Status
Accepted (2026-07-23, Phase 1 interview)

## Context
ADR 0005 (auth) and ADR 0006 (multi-tenancy) fixed the high-level shape; Phase 1
needs the concrete data model for tenants, principals, credentials, and onboarding.
The interview settled the open implementation choices.

## Decision

**Principals (unified, table-per-hierarchy).** A single `Principals` table holds an
abstract `Principal` (`Id`, `TenantId?`, `DisplayName`, `CreatedAt`,
`ConcurrencyToken`) with a `PrincipalType` discriminator over `User` and `Group`.
One id space means ownership and ACL grants (ADR 0007) have a single unambiguous FK
target. TPH chosen over TPT for one-table reads and clean cross-provider behaviour.

**Users.** `Email` + `NormalizedEmail` (upper-invariant), `PasswordHash` (null until
activation), `SecurityStamp`, `Status` (`Invited`/`Active`/`Disabled`), `TenantRole?`
(`Member`/`Admin`), `LockoutEnd?`, `AccessFailedCount`.

- **Platform administrator = a `User` with `TenantId = null`** (no separate table).
  `IsPlatformAdministrator` is derived from a null tenant. Consequence: a person who
  is both a platform admin and a tenant user holds two accounts — consistent with
  global-unique email.
- **Login email is globally unique** (ADR 0006), enforced by a unique index on
  `NormalizedEmail`. Uniqueness uses a **normalized shadow column**, not a database
  collation, for identical behaviour on PostgreSQL and SQLite (ADR 0001). Tenant is
  derived from the authenticated principal — no tenant in any URL.

**Groups.** `NormalizedName`, unique per `TenantId`. **Nested**: a
`GroupMembership(GroupId, MemberId)` edge's member is any principal — a user or
another group. Effective ACL rights resolve transitively (ADR 0007). Membership
**cycles are rejected in `SimplCalConDbContext.SaveChanges`** (see ADR 0017),
mirroring the sibling project's DbContext-invariant convention.

**App passwords** (ADR 0005). `AppPassword(Id, UserId, Label, PasswordHash,
CreatedAt, LastUsedAt?, RevokedAt?, ConcurrencyToken)` — per-device DAV credentials,
hashed at rest, individually revocable. **Full DAV access as the owning user** (no
per-collection scoping in v1). Cascade-deleted with the user.

**Onboarding & reset tokens.** `Token(Id, UserId, TokenHash, Purpose, ExpiresAt,
ConsumedAt?, IssuedByPrincipalId)` — single-use, expiring, hash-only. A tenant admin
creating a user mints an **activation** token; the admin delivers the link
out-of-band (SMTP delivery is Phase 3). Same entity serves **password reset**.

**Bootstrap.** The first platform administrator is **seeded from configuration on
first run** (idempotent: created only if no platform admin exists), which then allows
the first tenant to be created. No public setup window.

**Deferred:** service-account (non-human) principals — shared calendars are handled
by user ownership + group sharing (ADR 0007); the model stays users + groups in v1.

## Consequences
- Credential-management code (password hashing via `PasswordHasher<T>`, lockout,
  token issue/redeem) and the OpenIddict server wiring are the next Phase 1 unit;
  this ADR is the data foundation they build on.
- The identity infrastructure uses Microsoft's `PasswordHasher<T>` primitive but our
  own entities/stores (not full ASP.NET Core Identity), so the schema above is
  authoritative rather than framework-dictated.
- Because platform admins are tenant-less users, any "list users in tenant" query
  filters on a non-null `TenantId`; platform-admin management is a separate surface.
