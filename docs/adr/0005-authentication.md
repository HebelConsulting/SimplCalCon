# ADR 0005 — Authentication: OIDC for web/REST, per-device app passwords for DAV

## Status
Accepted (2026-07-23, spec interview)

## Context
Native DAV clients (iOS/macOS accounts, DAVx⁵, Thunderbird) generally support only
HTTP Basic against custom servers — no OAuth/OIDC flows. The web UI and REST API
should use modern token-based auth. Handing the real account password to every
device is unacceptable (no per-device revocation, password at rest in device
configs).

## Decision
- **Web UI + REST**: OpenID Connect via **OpenIddict** (authorization code + PKCE),
  local accounts per tenant. Tokens carry tenant + principal claims.
- **DAV**: HTTP **Basic over HTTPS** with **app passwords** — generated
  high-entropy credentials a user creates per device (named, e.g. "iPhone"),
  individually revocable, hashed at rest, usable *only* on the DAV surface (an app
  password cannot log in to the web UI or call `/api`).
- The account password itself is **never** accepted on the DAV surface.
- Login-flow hardening (rate limiting, lockout) applies to both password and app
  password verification; app-password last-used timestamps are shown in the web UI.

## Consequences
- Users must create an app password before configuring a device — the web UI's
  app-password page (with per-platform setup instructions) is a Phase 1 deliverable.
- Basic-per-request verification must be cheap: verified app passwords use a keyed
  fast hash / short-lived server-side cache so a strong slow hash (used at creation
  and cold verification) doesn't run on every DAV request.
- External IdP federation is deferred (ADR 0006); nothing in the token/claims design
  may preclude adding it later.
