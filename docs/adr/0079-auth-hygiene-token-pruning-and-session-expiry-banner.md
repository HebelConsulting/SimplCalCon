# ADR 0079 — Auth hygiene: OpenIddict token pruning + session-expiry banner

## Status

Accepted — implemented. Follow-on to ADR 0076 (SPA refresh tokens + redirect-to-login on expiry).

## Context

Two loose ends from ADR 0076:

1. **Refresh tokens accumulate.** With rolling refresh tokens, every silent renewal marks the old token
   redeemed and issues a new one. Those redeemed rows stay in the `OpenIddictTokens` table forever —
   nothing prunes them, so the table grows unbounded over a long-lived deployment.
2. **Session expiry is silent.** When renewal ultimately fails, `SessionExpiredHandler` bounces the user
   to login with no explanation — they just find themselves back at the sign-in screen.

## Decision

**Token pruning — a background service, on by default.** A `TokenPruneService` (`BackgroundService`,
mirroring `RetentionSweepService`) periodically calls `IOpenIddictTokenManager.PruneAsync(threshold)`
then `IOpenIddictAuthorizationManager.PruneAsync(threshold)` (that order is required — an authorization
with tokens still attached isn't pruned). It resolves the scoped managers via a per-cycle DI scope.

- Config `SimplCalCon:Auth` → `TokenPruneOptions`: **`TokenPruneDays` default 14** (matches the
  refresh-token lifetime), `PruneHours` default 24. **On by default** because pruning is non-destructive
  — `PruneAsync` only removes rows already marked **invalid/expired** (redeemed rolling-refresh tokens,
  dead authorizations), never a valid active session — and matches OpenIddict's own always-prune
  guidance. `TokenPruneDays = 0` fully opts out (returns before opening a scope; unit-guarded).
- **No new dependency** (OpenIddict's `PruneAsync` is used directly — Quartz isn't needed) and **no
  schema change** (deletes existing rows in the OpenIddict tables, which already live in
  `SimplCalConDbContext` via `UseOpenIddict()`).

**Session-expiry banner.** When `SessionExpiredHandler` redirects (on `AccessTokenNotAvailableException`
or a `401`), it sets an in-memory `SessionState.SessionExpired` flag before `NavigateToLogin`. The login
page (`Authentication.razor`, action `login`) reads the flag once, clears it, and shows a brief
**"⚠ Your session expired — signing you back in…"** banner above `RemoteAuthenticatorView`. A flag (not a
query param) keeps `NavigateToLogin`'s return-URL handling intact and is trivially testable.

## Consequences

- The `OpenIddictTokens` table self-maintains; a busy instance no longer accumulates dead token rows.
- Pruning is safe-by-default: only invalid/expired rows go, so enabling it can't sign anyone out.
- The expiry redirect now explains itself. The banner is brief (the auth view redirects to the IdP
  quickly), so it's a light touch rather than a modal interruption — deliberately, to avoid delaying the
  re-login. The durable "why am I here" cue is acceptable as a flash on the way through.

## Deferred

- A more prominent/persistent expiry notice (e.g. carried onto the IdP `/Account/Login` page, or a
  post-re-login toast) if the brief banner proves too subtle.
- Tuning `PruneAsync` batch behaviour if a very large token table ever makes a single prune call heavy
  (OpenIddict batches internally today).
