# ADR 0018 — Phase 1 authentication services

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
ADR 0005 fixed the auth model (OIDC for web/REST, per-device app passwords for DAV);
ADR 0016 fixed the identity data model. This ADR records the concrete auth-services
build: the OIDC server, the DAV credential path, password/lockout policy, and
first-run bootstrap. Scope was deliberately limited to the auth server + DAV auth +
bootstrap with thin proof endpoints; the ADR 0009 REST plumbing and resource
controllers are a later unit.

## Decision

**OIDC server (OpenIddict 7.x, Apache-2.0).** Authorization-code + PKCE (required) +
refresh + logout, over the EF core stores added to `SimplCalConDbContext`
(`options.UseOpenIddict()`; four `OpenIddict*` tables). Endpoints: `/connect/authorize`,
`/connect/token`, `/connect/logout`, `/connect/userinfo`. Access-token lifetime 15
minutes; refresh tokens rotate. The Blazor WASM app is a **public** client seeded at
startup (`simplcalcon-spa`). Tokens carry `sub`, `email`, `name`, `tenant_id`, and a
role claim (`platform_admin` or the tenant role). The interactive login is a cookie
session established by a minimal `/Account/Login` page (re-dressed when the web UI
lands). Every token exchange re-loads the user and refuses if it is no longer Active.

**Signing/encryption keys.** Development uses OpenIddict **ephemeral in-memory** keys
(zero setup; tokens don't survive a restart) and disables the HTTPS-transport
requirement so the endpoints work over plain HTTP locally and in tests. Production
**requires** a signing + encryption certificate from configuration and fails fast at
startup if absent.

**DAV credential path.** An `AuthenticationHandler` implements HTTP Basic on `/dav`,
backed by app passwords only (never the account password). Verification slow-hashes
(PBKDF2) against the user's active app passwords on a cache miss, then caches the
resulting identity keyed by a fast hash of (email, secret) for a short TTL, so
repeated polling skips the slow hash (ADR 0005). The raw secret is never cached;
`LastUsedAt` is stamped on the slow-path verify.

**Password hashing & policy.** Account and app-password secrets are hashed with the
framework `PasswordHasher<T>` (PBKDF2), with transparent rehash-on-login. Policy is
length-first (min 12) + a common-password denylist; lockout after N failed attempts
(default 5) for a cooldown (default 15 min), tracked by the `AccessFailedCount` /
`LockoutEnd` fields. Activation/reset tokens are stored as SHA-256 hashes (the inputs
are already high-entropy), single-use and expiring (activation 7 days, reset 2 hours).

**Provider selection & bootstrap.** The Api host picks the EF provider from
configuration (`SimplCalCon:Database:Provider` = `Sqlite`|`Postgres`) and supplies it
to `AddSimplCalConInfrastructure`; Infrastructure stays provider-agnostic (ADR 0017).
A hosted service runs migrations, seeds the SPA client, seeds the platform admin if
none exists (password from config → Active; else Invited + activation link logged),
and — in **Development** only — an optional demo tenant + admin so tenant-scoped
sign-in is exercisable.

## Consequences
- The auth vertical slice is exercised end-to-end by integration tests: the full
  code+PKCE flow (login → authorize → token → userinfo) and DAV Basic against a
  seeded app password, plus unit tests for lockout, activation redemption, and
  app-password verification.
- The thin endpoints (`/Account/Login`, `whoami`, `userinfo`) intentionally carry no
  house-style envelope yet; they are placeholders for the ADR 0009 REST unit and lock
  in no URL contract beyond the OIDC/DAV protocol paths.
- OpenIddict's ASP.NET `HttpContext` helpers live in the `Microsoft.AspNetCore`
  namespace (not `OpenIddict.Server.AspNetCore`) — a noted gotcha for the controllers.
- WebApplicationFactory host startup is CPU-contention-sensitive on cold migration;
  the integration assembly disables test parallelization to stay deterministic.
