# ADR 0076 — SPA refresh tokens + redirect-to-login on session expiry

## Status

Accepted — implemented. Fixes the reported "web client goes 401 after being left open idle, only a full
reload recovers it" bug. Extends the auth design in ADR 0005/0018.

## Context

Two independent gaps combined to strand an idle SPA tab:

1. **Nothing durable renewed the session.** The Blazor WASM client requested `openid email profile
   simplcalcon.api` but **not `offline_access`**, so — despite the OpenIddict server *and* the seeded
   `simplcalcon-spa` client both enabling the refresh-token grant — **no refresh token was ever issued**.
   The access token lives 15 minutes, and the only renewal path was Blazor's silent hidden-iframe
   re-authorization, which depends on the interactive `SimplCalCon.Auth` cookie (1 h **sliding**). On an
   idle tab nothing hits the authorize/cookie endpoint, so the sliding cookie is never refreshed and dies
   at ~1 h; after that silent renew fails.

2. **The failure wasn't handled.** There was no `AccessTokenNotAvailableException` handling and no global
   401 handler anywhere in the client. `RedirectToLogin` only fires on a route-level `NotAuthorized`,
   which is not re-evaluated for an already-rendered page whose access token merely expired. So the
   failure surfaced as raw 401s with no redirect — stuck until a manual reload restarted the OIDC flow.

## Decision

Fix both gaps; keep a **14-day sliding** session (the OpenIddict default).

**1. Durable renewal via refresh tokens.** The SPA now requests **`offline_access`**
(`Program.cs` `DefaultScopes`). With a refresh token present, the WASM auth library renews via the
**refresh-token grant** at the token endpoint — no dependency on the interactive cookie or a hidden
iframe — so the session survives long idle **and** browser restarts. The token exchange already
re-checks the account is Active on every refresh (`AuthorizationController.Exchange` →
`FindActiveUserAsync`), so a deactivated user's refresh fails with `invalid_grant`.

- **No server permission change or reseed needed.** `offline_access` is a built-in scope gated only by
  the client's `RefreshToken` grant permission, which the seeded client already has, and the controller
  already propagates requested scopes (`identity.SetScopes(...)`) — so requesting the scope was the only
  missing piece. (Verified by an integration test: with `offline_access` a refresh token is issued and
  renews; without it, none is.)
- Refresh-token lifetime is pinned explicitly to **14 days** (`SetRefreshTokenLifetime`), rolling by
  default (each renewal rotates the token and re-slides the window). The refresh token lives in the
  browser's session storage — the standard tradeoff for a public SPA client.

**2. Graceful redirect when renewal ultimately ends.** A centralized **`SessionExpiredHandler`**
(`DelegatingHandler`) wraps the auth handler on the `/api` client (registered first = outermost). It
redirects to the interactive login — capturing the current page as the return URL via
`NavigateToLogin("authentication/login")` — when the auth handler throws
`AccessTokenNotAvailableException` (silent renewal failed outright) **or** a response comes back `401`
(a still-attached but expired/rejected token). It no-ops while already on the auth pages, so concurrent
401s don't loop. This replaces the dead-end 401 with a clean re-login, and after signing in the user
lands back where they were.

## Consequences

- An idle tab now stays signed in for the refresh-token lifetime (14 days of *no* use; active use renews
  indefinitely), and survives a browser restart — the reported bug is gone.
- When the session truly ends (refresh token expired, or account deactivated), the app bounces to login
  instead of showing broken 401s.
- **Dev/demo caveat:** OpenIddict uses ephemeral keys in Development (ADR 0005), so a server restart
  invalidates existing refresh tokens → re-login after a restart (already true for access tokens).
  Production persistent keys keep them valid.
- The `LiveUpdates` SignalR connection benefits automatically: its `AccessTokenProvider` is re-invoked on
  each (re)connect and now gets a silently-renewed token.

## Deferred

- Enabling OpenIddict token pruning (rolling refresh tokens accumulate redeemed rows in the `Tokens`
  table without a background prune) — an operational cleanup, not a correctness issue.
- A visible "your session expired" toast before the redirect (currently a silent bounce to login).
- Absolute (non-sliding) session cap / idle-timeout policy if a stricter posture is ever wanted.
