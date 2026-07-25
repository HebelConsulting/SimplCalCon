# SimplCalCon

**A multi-tenant CalDAV / CardDAV server with a REST API and a Blazor WebAssembly web UI** — store
and synchronise calendar entries (events & tasks) and contacts across your devices (phones, tablets,
computers), plus edit, view, share, import/export and back them up from the browser.

Built on **.NET 10** with **Clean Architecture** and **EF Core**, running on **PostgreSQL _or_
SQLite** (both are first-class production databases — every feature works on both).

> ### 🧪 A showcase
> This repository is a **showcase of how a senior, AI-driven software developer can design and build
> a complex, production-shaped system in a remarkably short period of time** — a full CalDAV/CardDAV
> protocol stack, a REST API, a web client, multi-tenancy, sharing/ACLs, scheduling, data
> portability, and Docker/Kubernetes packaging, developed collaboratively and incrementally, with an
> Architecture Decision Record behind every choice.

---

## Features

- **CalDAV & CardDAV** (in-house protocol layer) — syncs with Apple Calendar/Contacts, Thunderbird,
  iOS, DAVx5, and other standard clients, with **WebDAV-Push** so DAVx5 gets pushed changes
  instead of polling.
- **REST API** — HATEOAS envelopes, RFC 7807 problem details, ETag/`If-Match` concurrency, JSON.
- **Blazor WebAssembly web UI** — calendars (list + month/week grid with **recurring events**),
  contacts (master-detail), import/export, sharing, trash & version history, profile & contact
  photos, with **live updates** (SignalR) so changes and invitations appear without a reload.
- **Multi-tenancy** with platform / tenant admins and **full ACL sharing**.
- **Authentication** — OIDC (auth code + PKCE) for web/REST; per-device app passwords for DAV.
- **Data portability** — per-collection import/export, account takeout, direct **Google export
  `.zip`** import (recreating the original calendars), and revocable read-only **subscription feeds**
  (`.ics`/`webcal`) that any client — including **Microsoft Outlook** — can subscribe to.
- **Scheduling** — attendees, free/busy, and RFC 6638 iTIP with web **invitations** (accept / tentative / decline) and **email iMIP** to *and from* external attendees (per-tenant SMTP out; inbound via a REST endpoint or IMAP polling).

See [`docs/spec.md`](docs/spec.md) for the full specification and
[`docs/adr/`](docs/adr/README.md) for the decision records.

---

## Quick start (Docker Compose)

The whole demo runs from the single `docker-compose.yaml` at the repository root — under **Docker**
(`docker compose`) or **Podman** (`podman compose`):

```bash
docker compose up --build -d
```

Then open **http://localhost:9080** and sign in with a seeded demo account:

| Account | Email | Password |
| --- | --- | --- |
| Platform admin | `admin@simplcalcon.local` | `ChangeMe-Platform-2026` |
| Demo-tenant admin | `admin@demo.local` | `ChangeMe-Demo-2026` |

Native (Apple) CalDAV/CardDAV clients reach the server over HTTPS at **https://localhost** (a Caddy
TLS proxy on port 443). Full walkthrough — running the stack, connecting native clients, and the
optional pgAdmin — is in the **[user & operator manual](docs/manual.md)**.

> The Compose stack ships in **Development / demo** configuration (ephemeral keys, demo seeding,
> throwaway credentials). It is **not for production** — see below.

---

## Production installation

The demo Compose file is for local/demo use. For a real deployment:

- **Container image:** published to `ghcr.io/hebelconsulting/simplcalcon` on GitHub Container
  Registry — full multi-arch (amd64 + arm64) on `v*` release tags, amd64 for `main`. The app listens
  on **port 9080**. Pull and run a release:

  ```bash
  docker pull ghcr.io/hebelconsulting/simplcalcon:0.1.0
  docker run -p 9080:9080 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e SimplCalCon__Database__Provider=Sqlite \
    -e SimplCalCon__Database__ConnectionString="Data Source=/data/simplcalcon.db" \
    -v simplcalcon-data:/data \
    ghcr.io/hebelconsulting/simplcalcon:0.1.0
  ```

  Tags: `0.1.0` (exact), `0.1` (minor), `main` (latest `main`), and `sha-<commit>`. Production also
  needs the OIDC certificates and `SimplCalCon:SpaClient:BaseUrl` below.
- **Kubernetes (Helm):** a chart lives at [`deploy/helm/simplcalcon`](deploy/helm/simplcalcon) —
  Deployment with startup/liveness/readiness probes and a non-root security context, a Service on
  9080, a config Secret, an optional Ingress, and a mount hook for the OIDC certificates.
- **Database:** choose the provider with `SimplCalCon:Database:Provider` (`Postgres` or `Sqlite`) and
  supply the connection string; point it at a managed/persistent database.
- **Certificates:** production **requires OIDC signing + encryption certificates** from config and
  **fails fast without them** (Development uses ephemeral in-memory keys).
- **Environment:** set `ASPNETCORE_ENVIRONMENT=Production`, and set `SimplCalCon:SpaClient:BaseUrl`
  to the origin the web UI is served from (the SPA's OIDC redirect must match it).
- **Health:** `/health/live` (liveness) and `/health/ready` (readiness) are anonymous.

---

## Documentation

- **[User & operator manual](docs/manual.md)** — running the stack, connecting native clients,
  pgAdmin.
- **[Specification](docs/spec.md)** — the full functional/architectural spec.
- **[Architecture Decision Records](docs/adr/README.md)** — the rationale behind every decision.
- **[CLAUDE.md](CLAUDE.md)** — how the repository is structured and operated.

---

## Tech stack

.NET 10 · ASP.NET Core · EF Core (PostgreSQL + SQLite) · OpenIddict · Blazor WebAssembly · Serilog ·
Ical.Net / FolkerKinzel.VCards · xUnit · Stryker.NET (mutation testing) · Docker / Podman · Helm · Caddy.

## License

Licensed under the **Apache License 2.0** — see [`LICENSE`](LICENSE).
