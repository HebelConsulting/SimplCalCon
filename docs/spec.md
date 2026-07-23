# SimplCalCon — Specification

SimplCalCon is a multi-tenant server for storing and synchronizing **calendar entries
(events and tasks)** and **contacts** across all of a user's devices — smartphones,
tablets, and computers — plus a web interface for viewing, editing, and backing up the
data.

This document is the top-level specification: vision, scope, requirements, and phases.
Each architectural decision behind it is recorded as an ADR under `docs/adr/`
(index: `docs/adr/README.md`). Where this document and an ADR disagree, the newer
document wins and the older one must be updated in the same PR.

---

## 1. Vision & goals

- **Native device sync, no client software to install.** Devices sync through the
  standard **CalDAV** (calendars/tasks) and **CardDAV** (contacts) protocols, so the
  stock apps on iOS/macOS, DAVx⁵ on Android, Thunderbird, Outlook, etc. work out of
  the box (ADR 0002).
- **A first-class web UI** (Blazor WASM, ADR 0010) for editing, viewing, sharing, and
  backing up data — served by the same server.
- **Multi-tenant**: multiple isolated organizations on one deployment, with
  platform-level and tenant-level administration (ADR 0006).
- **Data safety**: per-object version history and a restorable trash (ADR 0011), full
  export/takeout, and server-to-server migration for onboarding (ADR 0013).
- **Open source (Apache 2.0)** with only Apache-2.0-compatible dependencies, enforced
  in CI (ADR 0015).

Non-goals for the product as a whole: e-mail hosting, chat, file sync, or a native
desktop/mobile app (desktop and mobile are covered by DAV-speaking native apps and
the web UI).

## 2. Actors

| Actor | Description |
|---|---|
| Platform administrator | Operates the deployment; creates and manages tenants. |
| Tenant administrator | Manages users, groups, and policies inside one tenant. |
| User | Owns calendars/address books; shares them; syncs devices. |
| Group | Set of users within a tenant; usable as an ACL principal. |
| Device | A DAV client authenticated with a per-device app password. |

## 3. Domain model (conceptual)

- **Tenant** — isolation boundary; owns users, groups, and all their data.
- **Principal** — a user or group; the subject of ACL grants and DAV's
  principal resources.
- **Collection** — either a **Calendar** (holds events and tasks) or an
  **Address book** (holds contacts). Owned by exactly one user; shareable via ACLs.
  Users can have any number of each; a default calendar and address book are
  provisioned on account creation.
- **Object** — a calendar object (one iCalendar `VEVENT` series or `VTODO`) or a
  contact (one vCard). Stored as the original blob (source of truth) plus indexed
  fields for querying (ADR 0004). Each object carries a server-assigned stable UID,
  a per-revision ETag, and its full revision history (ADR 0011).
- **App password** — a generated, individually revocable credential a user creates
  per device for DAV Basic authentication (ADR 0005).

## 4. Functional requirements

### 4.1 Synchronization (DAV surface) — ADR 0002, 0003

- CardDAV per RFC 6352 and CalDAV per RFC 4791 over WebDAV (RFC 4918), implemented
  in-house on ASP.NET Core (ADR 0003).
- Discovery: `/.well-known/caldav` and `/.well-known/carddav` redirects,
  `current-user-principal`, calendar/address-book home sets — the path stock iOS and
  macOS account setup follows must work end to end.
- Efficient sync: CTag (`getctag`), **sync-collection / sync-token** (RFC 6578),
  multiget and query REPORTs, per-object ETags with `If-Match` semantics.
- **WebDAV-Push in v1** (ADR 0012) so capable clients (DAVx⁵, Tasks.org) get
  near-instant updates; everything also works on pure polling.
- Time zones by reference (`TZID` + `VTIMEZONE` round-tripping); recurring events
  with full RRULE/EXDATE/`RECURRENCE-ID` override support.

### 4.2 Calendar feature scope — ADR 0008

- **Events (`VEVENT`)** including recurrence, exceptions, all-day events, alarms
  (`VALARM` stored/round-tripped; the server does not send alarm notifications).
- **Tasks (`VTODO`)** including recurrence and completion state.
- **Scheduling, internal-first** (RFC 6638): invite other users on the same server,
  attendee inbox/outbox, accept/decline/tentative with organizer updates, and
  free-busy lookup. **iMIP (e-mail invitations to external attendees) is Phase 3** —
  the data model reserves attendee status for external participants from day one.
- `VJOURNAL` is out of scope (stored blobs are preserved if a client writes one, but
  no indexing/UI support).

### 4.3 Contacts

- vCard 3.0 and 4.0 storage and round-tripping; contact groups
  (`KIND:group` / `X-ADDRESSBOOKSERVER-KIND`); photos.
- Indexed fields for search/autocomplete: display name, given/family name, emails,
  phones, organization.

### 4.4 REST API & web UI — ADR 0009, 0010

- REST API under `/api` following the house conventions: HATEOAS envelopes, RFC 7807
  problem details with typed exception hierarchy, media-type versioning,
  ETag/`If-Match` on every mutation, JSON only (no XML negotiation — ADR 0009),
  OpenAPI document + Scalar UI in development.
- Web UI (Blazor WASM): calendar views (month/week/day/agenda), task lists, contact
  browsing/editing, collection management, sharing (ACL) management, trash and
  version-history restore, import/export, app-password management, tenant/platform
  admin screens. Live updates via a SignalR channel (ADR 0012).

### 4.5 Sharing & access control — ADR 0007

- Full ACL model: per-collection grants of fine-grained rights (read, write-content,
  create, delete, share, admin) to principals (users or groups), plus tenant-level
  roles (tenant admin) and platform-level roles (platform admin).
- Exposed on both surfaces: web UI/REST as first-class sharing management, DAV as
  read-only `DAV::acl`/`current-user-privilege-set` reporting (DAV `ACL` method is
  not a mutation surface in v1).

### 4.6 Administration — ADR 0006

- Platform admins: tenant lifecycle (create/suspend/delete), platform diagnostics.
- Tenant admins: user lifecycle (invite/registration flow, disable, delete with
  takeout), group management, tenant-level defaults (retention windows, quotas).
- All accounts are local in v1; external IdP federation is explicitly deferred.

### 4.7 Data safety & portability — ADR 0011, 0013

- **Version history**: every revision of every object is retained and restorable;
  retention/pruning policy is tenant-configurable.
- **Trash**: deleted objects and deleted collections go to a trash, restorable via
  web UI/REST within a retention window (default 30 days), then purged. DAV clients
  observe a normal delete.
- **Import/export**: upload `.ics`/`.vcf` into a collection; download any collection
  as a single file.
- **Takeout**: one archive with all collections of a user or a whole tenant.
- **Server-to-server migration**: a built-in DAV *client* that pulls all collections
  from an existing CalDAV/CardDAV server (Google, Nextcloud, iCloud, …) into
  SimplCalCon.

### 4.8 Authentication — ADR 0005

- Web UI and REST: OpenID Connect via OpenIddict (authorization code + PKCE),
  local accounts.
- DAV: HTTP Basic with **per-device app passwords** — generated, named, individually
  revocable, hashed at rest; never the account password.

## 5. Non-functional requirements — ADR 0001, 0014, 0015

- **Scale target (medium)**: hundreds of tenants, thousands of users per deployment,
  calendars with tens of thousands of events; PostgreSQL is the primary production
  database, SQLite a supported configurable alternative for small installs and the
  test-parity engine (ADR 0001). Every schema/query must work on both.
- **Performance guardrails**: sync-collection and time-range queries answered from
  indexed fields, never by parsing blobs at request time; recurrence expansion
  bounded by an indexed occurrence window.
- **Deployment**: single container (multi-stage Alpine Dockerfile, non-root),
  `docker-compose.yaml` shared between Docker and Podman, Kubernetes-ready with
  `/health/live` + `/health/ready` (ADR 0015).
- **Quality gates**: warnings-as-errors repo-wide, license allowlist CI gate, the
  bare-`ApiException` guard test pattern (ADR 0015).
- **Interoperability acceptance**: a change to the DAV surface is not done until it
  passes the client matrix — iOS/macOS accounts, DAVx⁵, Thunderbird — for the flows
  it touches (tracked as an E2E test suite plus a manual matrix where automation
  can't reach).

## 6. Phases

- **Phase 1 — Sync core**: solution skeleton, tenancy + principals + auth
  (OIDC, app passwords), collections + hybrid object storage, CardDAV + CalDAV
  (events/tasks) with discovery, ETags, CTag, sync-token; ICS/VCF import/export;
  REST read surface; minimal web UI (login, collection list, app passwords).
- **Phase 2 — Product**: full web UI (calendar/task/contact editing), ACL sharing on
  both surfaces, trash + version history + restore, WebDAV-Push + SignalR, internal
  scheduling (RFC 6638) + free-busy, takeout, admin screens.
- **Phase 3 — Reach**: iMIP e-mail invitations (SMTP out, reply ingestion),
  server-to-server migration, tenant quotas/retention policies, external IdP
  federation (re-evaluated).

Each phase item becomes ADRs + implementation PRs; docs are updated in the same PR
that lands the code (see CLAUDE.md).
