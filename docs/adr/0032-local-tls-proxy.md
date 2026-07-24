# ADR 0032 — Local HTTPS via a Caddy TLS reverse proxy (as built)

## Status
Accepted (2026-07-24). Extends the deployment setup ([ADR 0024](0024-deployment-and-ci-foundation.md)).

## Context
Native Apple CalDAV/CardDAV clients (Contacts, Calendar) refuse plain-HTTP accounts —
they require HTTPS — so they couldn't connect to the demo stack on `http://localhost:9080`.
(Thunderbird, more lenient, works over HTTP.) The app serves HTTP only; adding an in-app
dev certificate is fiddly and per-environment.

## Decision
Add a **Caddy** service to `docker-compose.yaml` that terminates TLS and reverse-proxies to
the API, using Caddy's **internal CA** (`tls internal`) so no external certificate authority
is involved:

- `deploy/Caddyfile`: `localhost { tls internal; reverse_proxy api:9080 }`.
- Compose `proxy` service (`caddy:2-alpine`), host **443** → Caddy, with named volumes for
  its CA/state. The API keeps `9080` exposed.

**Scope (interview outcome): HTTPS for DAV only; the web UI + OIDC stay on
`http://localhost:9080` unchanged** — no OIDC redirect re-seeding, no DB reset. Native
clients use `https://localhost`; the SPA login stays wired to its `:9080` origin.

**No app change needed:** the DAV surface uses **relative** redirects/hrefs
(`.well-known/caldav` → `Location: /dav/`, hrefs like `/dav/calendars/...`), so they resolve
against the client's `https` scheme automatically — no `X-Forwarded-Proto` handling.

**Trusting the CA:** extract Caddy's root once
(`docker compose exec proxy cat /data/caddy/pki/authorities/local/root.crt`) and trust it on
the client (macOS Keychain → Always Trust). Then Apple clients accept `https://localhost`.

## Consequences
- Native Apple CalDAV/CardDAV clients connect over `https://localhost` (verified: TLS
  handshake, `.well-known/caldav` 301, authenticated `PROPFIND /dav/` 207).
- One shared compose file still runs under docker and podman — **caveat**: rootless Podman
  can't bind 443 by default (run rootful, or lower `net.ipv4.ip_unprivileged_port_start`).
- Demo-only trust model (a local self-signed CA the user installs). Production uses a real
  certificate / managed ingress (Helm chart, ADR 0024), not this proxy.
- Deferred: HTTPS for the web UI + OIDC behind the same proxy (needs the SPA client's
  redirect origin moved to `https://localhost`).
