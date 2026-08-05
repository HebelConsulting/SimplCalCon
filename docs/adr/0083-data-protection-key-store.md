# ADR 0083 — Persist the Data Protection key ring in the database

## Status

Accepted — implemented.

## Context

ASP.NET Core **Data Protection** provides the app's symmetric key ring. SimplCalCon uses it to
encrypt **per-tenant SMTP and IMAP passwords** at rest (ADR 0047 — `TenantEmailSettingsService`
`Protect`/`Unprotect`), and the cookie-auth stack (`/Account/Login`, ADR 0005) uses it implicitly to
protect the auth cookie.

Until now nothing configured Data Protection, so the app ran on the framework **default key ring**,
whose storage location is host-dependent and frequently **ephemeral**:

- In a container with no persistent, writable `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` equivalent,
  the ring lives only in memory and is regenerated on every restart.
- When the ring is lost, everything it encrypted becomes undecryptable. The code already anticipated
  this — `TenantEmailSettingsService` swallows the resulting `CryptographicException` and returns
  `null` ("keys rotated/lost, e.g. dev ephemeral keys after a restart"), i.e. a tenant's saved SMTP/IMAP
  password silently stops working after a restart, and every existing cookie session is invalidated.

OpenIddict's OIDC signing/encryption uses its own X.509 certificates (ADR 0005), so it is **not**
affected — this is specifically about the Data Protection key ring.

## Decision

Persist the key ring to the existing `SimplCalConDbContext` via
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` (MIT):

```csharp
services.AddDataProtection()
    .SetApplicationName("SimplCalCon")
    .PersistKeysToDbContext<SimplCalConDbContext>();
```

Registered in `AddSimplCalConInfrastructure` (`SimplCalCon.Infrastructure/DependencyInjection.cs`),
next to `AddDbContext` and the OpenIddict-core registration — the same layer as the only consumer
(`TenantEmailSettingsService`) and the DbContext owner. Infrastructure already carries the
`Microsoft.AspNetCore.App` framework reference, so `IDataProtector` was already in scope; the new
package is provider-agnostic and needs only the DbContext type.

- **Schema:** `SimplCalConDbContext` implements `IDataProtectionKeyContext` and exposes
  `DbSet<DataProtectionKey> DataProtectionKeys`, so the package's fixed **`DataProtectionKeys`** table
  (`Id` identity PK · `FriendlyName` text nullable · `Xml` text nullable) becomes part of the model and
  gets a migration in **both** provider projects (`AddDataProtectionKeys`, Sqlite + Postgres), applied by
  the existing bootstrap migrate step. No custom `IEntityTypeConfiguration` — EF conventions + the DbSet
  name suffice.
- **All environments** persist to the DB (including Development/demo), for one consistent code path;
  sessions and encrypted email passwords now survive restarts everywhere. The demo compose DB is still
  wiped when its volume is reset, which is expected.
- **`SetApplicationName("SimplCalCon")`** pins a stable application discriminator so the ring's purpose
  isolation is independent of the content-root path (the default discriminator).
- **Keys are stored as-is in `Xml`** (no `EncryptKeysWith`). The database is the trust boundary — the same
  posture as OpenIddict's own EF-stored data. This can be tightened later to `EncryptKeysWith` an X.509
  cert **without a schema change** if a deployment wants defense-in-depth.

## Consequences

- The Data Protection key ring survives restarts on both providers. Tenant SMTP/IMAP passwords stay
  decryptable and cookie sessions persist across restarts and across scaled-out instances sharing the DB.
- One new table (`DataProtectionKeys`) and one new MIT package
  (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`) — inside the license allowlist, no override.
- The `CryptographicException` fallback in `TenantEmailSettingsService` remains as a safety net for a
  genuinely rotated/lost key but should no longer fire on an ordinary restart.

## Deferred

- **`EncryptKeysWith`** (encrypt the stored key XML at rest with the OIDC / a dedicated cert) — deferred
  as an opt-in hardening; adds no schema, can be turned on later.
- Redis / distributed key-store backends — the shared DB already covers the medium-scale multi-instance
  target (ADR 0014).
