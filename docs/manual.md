# SimplCalCon — User & Operator Manual

Practical, task-oriented guidance for running SimplCalCon and connecting to it. For the
architecture and the rationale behind these features, see [`spec.md`](spec.md) and the ADRs under
[`adr/`](adr/README.md).

> The Docker Compose stack ships in **Development / demo** configuration (ephemeral keys, demo
> seeding, throwaway credentials). It is **not for production** — treat every password below as a
> demo default.

## Contents

- [Running the stack](#running-the-stack)
- [Database administration with pgAdmin](#database-administration-with-pgadmin)
- [Connecting native calendar & contacts clients (CalDAV/CardDAV)](#connecting-native-calendar--contacts-clients-caldavcarddav)

---

## Running the stack

The whole demo runs from the single `docker-compose.yaml` at the repository root. It works
unmodified under **Docker** (`docker compose`) and **Podman** (`podman compose`) — substitute the
command you have; everything below uses `docker compose`.

### Prerequisites

- Docker (with Compose v2) *or* Podman with `podman compose`.
- Ports **9080** and **443** free on the host. (Rootless Podman can't bind 443 by default — run
  rootful, or lower `net.ipv4.ip_unprivileged_port_start`.)

### Start

```bash
docker compose up --build -d
```

First start builds the API image, launches Postgres, applies migrations, and seeds the demo data.
Watch it come up with `docker compose logs -f api`; it's ready when `/health/ready` returns 200.

### Services & URLs

| Service | Purpose | Address |
| --- | --- | --- |
| `api` | Web UI + REST API + CalDAV/CardDAV | **http://localhost:9080** |
| `proxy` (Caddy) | HTTPS front for native clients (ADR 0032) | **https://localhost** (port 443) |
| `db` (Postgres) | Database — **not** published to the host | internal only |
| `pgadmin` | Optional DB admin UI (opt-in `tools` profile) | http://localhost:6050 — see [pgAdmin](#database-administration-with-pgadmin) |

Useful endpoints on the API: `/scalar` (interactive API docs, Development only),
`/openapi/v1.json`, and the `/health/live` + `/health/ready` probes (all anonymous).

### Sign in (seeded demo accounts)

Open **http://localhost:9080** and sign in with one of the seeded accounts:

| Account | Email | Password |
| --- | --- | --- |
| Platform admin | `admin@simplcalcon.local` | `ChangeMe-Platform-2026` |
| Demo-tenant admin | `admin@demo.local` | `ChangeMe-Demo-2026` |

These are **demo defaults** — change them for anything beyond local use.

### Everyday commands

```bash
docker compose ps               # what's running
docker compose logs -f api      # follow the API logs
docker compose restart api      # restart one service
docker compose stop             # stop the stack (keeps data)
docker compose up --build -d    # rebuild + apply code changes
```

### Stop & reset

```bash
docker compose down             # stop and remove containers (data volumes are kept)
docker compose down -v          # also delete volumes — wipes the database and starts fresh
```

The named volumes (`db-data`, `caddy-data`, `caddy-config`) persist across `up`/`down`, so your
data and the trusted TLS cert survive restarts until you `down -v`.

---

## Database administration with pgAdmin

An optional **pgAdmin 4** web UI is bundled for inspecting the development Postgres database. It is
**opt-in** — gated behind the `tools` Compose profile, so it never runs in the default stack.

### Start it

```bash
docker compose --profile tools up -d pgadmin
# podman: podman compose --profile tools up -d pgadmin
```

Then open **http://localhost:6050**.

### Access details

| What | Value |
| --- | --- |
| URL | http://localhost:6050 |
| pgAdmin login | **none** — it runs in *desktop mode* (`SERVER_MODE=False`): no login page, no master password |
| Pre-registered server | **SimplCalCon (demo)** — appears in the browser tree already **connected** |
| Connects as | the Postgres **`postgres`** superuser (password supplied automatically from a mounted passfile — you are never prompted) |
| Database | `simplcalcon` |

Just expand **SimplCalCon (demo) → Databases → simplcalcon → Schemas → public → Tables** — the tree
is connected on open, no clicks or passwords required.

### Credentials reference (demo only)

| Role | User | Password | Used by |
| --- | --- | --- | --- |
| Superuser | `postgres` | `postgres` | pgAdmin (browsing/administration) |
| Application login | `simplcalcon` | `simplcalcon` | the SimplCalCon API (`deploy/db-init.sql`) |

### Stop it

```bash
docker compose --profile tools stop pgadmin   # keep it around, stopped
# or remove it entirely:
docker compose --profile tools down           # stops the whole stack incl. pgadmin
```

pgAdmin keeps **no persistent volume** — it re-imports the pre-registered server fresh on every
start, so there is nothing to clean up and no stale state to fix.

> **Security note:** the auto-connect is a deliberate dev convenience that bakes in throwaway
> superuser credentials. Never enable this service, or reuse these credentials, in a real
> deployment.

---

## Connecting native calendar & contacts clients (CalDAV/CardDAV)

SimplCalCon speaks CalDAV and CardDAV, so calendars and contacts sync with Apple Calendar/Contacts,
Thunderbird, iOS, DAVx5, and other standard clients.

### Before you start

- **Authentication uses a per-device app password**, never your account password. Create one in the
  web UI under **Configuration**, and use your **account email** as the username.
- Native clients that require HTTPS (all Apple clients) connect through the bundled TLS proxy on
  **port 443** (ADR 0032). **Trust the proxy's root CA on the client first**, or the connection is
  refused:

  ```bash
  docker compose exec proxy cat /data/caddy/pki/authorities/local/root.crt
  ```

  Import that certificate into the OS trust store (macOS Keychain). **Thunderbird uses its own
  certificate store** — import the same root CA into Thunderbird separately.

### macOS Calendar (CalDAV) — verified

Add the account manually (**System Settings → Internet Accounts → Add Other Account → CalDAV
Account → Manual**):

| Field | Value |
| --- | --- |
| Account Type | Manual |
| User Name | your account email |
| Password | an **app password** (from **Configuration** in the web UI) |
| Server Address | `localhost` |
| **Port** | **`443`** — mandatory, must be entered explicitly |
| **Server Path** | **`/dav/`** |

The full URL is `https://localhost:443/dav/`. The `/dav/` path is enough — the server answers with
the current-user-principal, so macOS discovers your calendar home automatically; you do **not** need
to know or enter any user ID. macOS Calendar shows **all** your calendars.

### macOS Contacts (CardDAV) — with a known limitation

Add a **CardDAV** account the same way (User Name = email, Password = app password, Server Address =
`localhost`, Port `443`, Server Path `/dav/`).

> **Apple Contacts limitation:** macOS **Contacts.app shows only your default address book**
> (`contacts`), even though the server exposes all of them correctly. This is a documented behaviour
> of Contacts.app, **not** a server bug — it never enumerates a non-default book. **To use multiple
> address books, use Thunderbird, iOS, or DAVx5**, all of which show every book. (macOS Calendar has
> no equivalent limit.)

### Thunderbird / iOS / DAVx5

These clients discover everything from the same base URL (`https://localhost/dav/`) with your email
+ app password, and show **all** calendars and address books. Remember the Thunderbird-specific CA
import noted above.

### Finding your principal path (advanced)

The base `/dav/` path is normally all you need. If a client insists on an explicit principal URL, it
is `/dav/principals/{userId}/` — your `{userId}` is shown in the web UI; both forms work.
