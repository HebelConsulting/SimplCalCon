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

> **Devices need a reachable host + a trusted certificate.** `localhost` only works on the machine
> running the stack. A phone or another computer must reach the server by LAN IP or DNS name, over
> HTTPS, with a certificate its OS trusts — either a real (e.g. Let's Encrypt) certificate in
> production, or the demo Caddy root CA installed on the device. Replace `your-host` below accordingly.
> Good news: the DAV surface emits **relative** redirects/hrefs, so a client that connects by IP/name
> stays there — there is no bounce back to `localhost` (ADR 0032).

#### Demo LAN HTTPS override (for iOS/iPadOS)

> For a full step-by-step (cert export, per-platform trust, verify, tear down) see
> [`dav-device-testing.md`](dav-device-testing.md).

The default demo proxy only serves `localhost`. Bring the stack up with the opt-in LAN override so the
proxy **also** serves your machine's LAN IP/hostname over HTTPS (`docker-compose.lan.yaml`):

```bash
LAN_HOST=10.0.2.23 docker compose -f docker-compose.yaml -f docker-compose.lan.yaml up -d
docker compose exec proxy cat /data/caddy/pki/authorities/local/root.crt   # install/trust on the device
```

Set `LAN_HOST` to your machine's IP (`ipconfig getifaddr en0` on macOS) **or**, better, its `.local`
mDNS hostname — a hostname avoids the IP-literal SNI limitation and iOS resolves `.local` via Bonjour.
Then trust the printed root CA on the device once (AirDrop/email the `.crt`; on iOS install the profile,
then **Settings → General → About → Certificate Trust Settings → enable full trust**). Only the proxy
changes — the web UI/OIDC stay on `localhost`; DAV uses app-password auth, so device sync just works.

### Thunderbird (CalDAV + CardDAV)

1. Import the proxy root CA into **Thunderbird's own** store: **Settings → Privacy & Security →
   Certificates → Manage Certificates → Authorities → Import** (Thunderbird ignores the OS store).
2. **Calendar:** **New Calendar → On the Network**; Username = your email, Location =
   `https://your-host/dav/`. Thunderbird discovers every calendar; pick which to add.
3. **Address book:** **Address Book → New → CardDAV Address Book**; Username = email, Location =
   `https://your-host/dav/`. Enter the app password when prompted. All books appear.

### iOS / iPadOS (Apple Calendar + Contacts)

1. Make the server reachable over trusted HTTPS: in the demo, use the **LAN HTTPS override** above
   (set `LAN_HOST`, trust the root CA on the device); in production, use a real cert + DNS name.
2. **Calendar:** **Settings → Calendar → Accounts → Add Account → Other → Add CalDAV Account.**
   Server = `<LAN_HOST>` (or your domain), User Name = email, Password = app password. (iOS derives
   the `/dav/` path and discovers your calendars.)
3. **Contacts:** **Settings → Contacts → Accounts → Add Account → Other → Add CardDAV Account**, same
   values. iOS shows all address books (unlike macOS Contacts).

Alternatively, skip the proxy/cert entirely with a tunnel (`cloudflared tunnel --url http://localhost:9080`),
which gives a public already-trusted `https://…` URL — handy for a one-off test.

### DAVx⁵ (Android)

1. Trust the certificate (real cert, or install the demo root CA via **Settings → Security →
   Encryption & credentials → Install a certificate → CA certificate**).
2. **Add account → Login with URL and user name.** Base URL = `https://your-host/dav/`,
   User name = email, Password = app password. DAVx⁵ discovers all calendars + address books; choose
   which to sync and set the sync interval.
3. For instant sync instead of polling, set up **WebDAV-Push** — see the next section.

### Instant sync with WebDAV-Push (DAVx⁵ + ntfy)

SimplCalCon implements **WebDAV-Push** (ADR 0052): when a calendar/contact changes on the server, it
pushes a notification to subscribed devices so they sync **within seconds**, instead of waiting for the
next poll. DAVx⁵ is the supported client.

**How it works.** DAVx⁵ delivers push over **UnifiedPush** (an open, Google-free push standard), so the
Android device needs a **UnifiedPush distributor** app. The simplest is **ntfy**. DAVx⁵ then registers
its push endpoint with SimplCalCon, and the server sends encrypted Web Push messages to it on every
change. (Apple Calendar/Contacts don't use this — they have their own push — and Thunderbird polls.)

**On the Android device (one-time):**

1. Install a UnifiedPush distributor — **ntfy** from F-Droid or the Play Store — and **open it once** so
   it registers as the system's push distributor. (By default it uses the public `ntfy.sh` relay; you
   can point ntfy at your own self-hosted ntfy server for privacy.)
2. Open DAVx⁵ and **sync the account once**. DAVx⁵ auto-detects that the server advertises push and
   registers automatically — no extra DAVx⁵ setting. You can now raise the DAVx⁵ sync interval (or set
   it to manual); push covers real-time updates.
3. **Verify:** change an event/contact from another client (the web UI or Thunderbird) — the Android
   device should update within seconds without you touching it.

**If the device doesn't update** (but the server side is fine — this is almost always device config):

- **Battery optimization** is the usual culprit — Android doze kills the ntfy background connection, so a
  delivered push never wakes DAVx⁵. Set **both ntfy and DAVx⁵** to **Unrestricted** battery use (Android
  Settings → Apps → the app → Battery), and keep the ntfy app connected (it shows a persistent
  "connected" notification).
- To confirm the **server** actually delivered (independent of the phone), poll the device's ntfy topic —
  `curl -s 'https://ntfy.sh/<topic>/json?poll=1&since=20m'` (the `<topic>` is the `up…` id from the
  device's endpoint). Base64 lines are the encrypted push messages that reached the push service; if
  they're there, the server did its job and the gap is on the device.
- **ntfy can't connect** (e.g. it shows *"Websocket not supported, the server may not respond or address
  might be incorrect"*): the ntfy app holds its live connection over a **WebSocket** to its push server,
  and if that fails, messages reach ntfy but never reach the phone. This is an ntfy-client/network issue,
  **independent of SimplCalCon** — check ntfy → **Settings → Default server** is exactly `https://ntfy.sh`
  (not a custom/self-hosted URL that doesn't speak WebSocket); check the phone's **network isn't blocking
  WebSocket** (test whether ntfy connects on cellular vs the LAN WiFi); and **update the ntfy app**. If
  the public relay stays unreachable, **self-host ntfy** on the LAN and point both ntfy's default server
  and DAVx⁵'s UnifiedPush distributor at it.
- With **ephemeral** dev keys, the VAPID pair changes on every server restart, which invalidates existing
  subscriptions — **re-sync DAVx⁵** after a restart. Production persistent keys don't have this problem.

> **Scope note:** SimplCalCon's job ends at delivering the encrypted push to the push service (ntfy).
> The last hop — push service → ntfy app → DAVx⁵ — is UnifiedPush/ntfy/device territory; the checks above
> are for that hop, not the server.

**On the server (operator) — enabling push:** WebDAV-Push is **off unless VAPID keys are present**
(Web Push signing keys). Configure them under `SimplCalCon:WebPush` (env-var form in parentheses):

| Setting | Purpose |
|---|---|
| `VapidPublicKey` (`SimplCalCon__WebPush__VapidPublicKey`) | VAPID public key — **required** for production push |
| `VapidPrivateKey` (`SimplCalCon__WebPush__VapidPrivateKey`) | VAPID private key — **required**; keep secret |
| `Subject` (`SimplCalCon__WebPush__Subject`) | VAPID contact, a `mailto:` or `https:` URL (defaults to a placeholder) |
| `AllowEphemeralKeys` (`SimplCalCon__WebPush__AllowEphemeralKeys`) | Dev only: generate a throwaway pair at startup (resets on restart → subscriptions drop) |
| `SubscriptionTtlDays` (`SimplCalCon__WebPush__SubscriptionTtlDays`) | How long a registration lasts before the device must re-register (default 30) |

- **Generate a production key pair** once (e.g. `npx web-push generate-vapid-keys`) and supply both keys
  via config/secrets. **Persist them** like the OIDC certificates — if the keys change, all existing
  device subscriptions become undeliverable and devices must re-register.
- **Development / demo:** `AllowEphemeralKeys=true` is already set (in `appsettings.Development.json`,
  and explicitly in `docker-compose.lan.yaml`), so the demo stack has push enabled out of the box with a
  throwaway key pair. The startup log confirms it: `WebDAV-Push: using EPHEMERAL VAPID keys …`.
- With **no keys and no ephemeral flag**, push is simply disabled (clients fall back to polling) — the
  startup log says `WebDAV-Push disabled: no VAPID key pair configured.`

### Microsoft Outlook (classic Windows) — CalDav Synchronizer add-in

Outlook has no native CalDAV/CardDAV (see [`outlook-gap-analysis.md`](outlook-gap-analysis.md)); the
open-source **Outlook CalDav Synchronizer** add-in provides two-way sync.

1. Install the add-in from <https://caldavsynchronizer.org/> and restart Outlook.
2. **CalDav Synchronizer → Synchronization Profiles → add → Generic CalDAV/CardDAV.**
3. Set the DAV URL to `https://your-host/dav/` (or a specific collection URL), Username = email,
   Password = app password, then **Test or discover settings** to pick the calendar/address book.
4. Choose the sync interval and direction (two-way), and add one profile per collection.
5. For a **read-only** view instead (any Outlook variant, no add-in), use the subscription feed below.

### Wire smoke test (before trying a client)

Confirm the deployment, app-password auth, and TLS work end-to-end with the bundled harness — it does
exactly what a client's first connection does (discovery → home-sets → a PUT/GET/REPORT/DELETE
round-trip):

```bash
scripts/dav-smoke.sh https://your-host you@example.com "your-app-password"
```

All green means a real client should connect with the same URL + credentials; a failure pinpoints
whether it's discovery, auth, or TLS before you blame the client. For a self-signed demo cert the
script skips TLS verification (set `DAV_SMOKE_STRICT_TLS=1` to enforce it against a real cert). For a
deeper look, enable the DAV wire trace (`Serilog__MinimumLevel__Override__SimplCalCon.Dav.Wire=Verbose`)
to log full request/response bodies (ADR 0033) — off by default, and never in production.

### Finding your principal path (advanced)

The base `/dav/` path is normally all you need. If a client insists on an explicit principal URL, it
is `/dav/principals/{userId}/` — your `{userId}` is shown in the web UI; both forms work.

### Subscribing to a read-only feed (incl. Microsoft Outlook)

Some clients can't do two-way CalDAV/CardDAV — most notably **Microsoft Outlook**, which has no native
CalDAV support (see [`outlook-gap-analysis.md`](outlook-gap-analysis.md)). For those, publish a
**read-only subscription feed**:

1. In the web UI, open a calendar (or address book) → **Edit** (owner only) → **Subscription** →
   **Enable subscription link**, then **Copy** the URL. It looks like
   `https://…/api/calendars/{id}/feed/{token}.ics` (`webcal://…` works too).
2. In the client, add an *internet calendar subscription* and paste the URL:
   - **Outlook (classic Windows / web / Outlook.com):** Add calendar → From internet → paste.
   - **Apple Calendar:** File → New Calendar Subscription. **Google Calendar:** Other calendars →
     From URL. **Thunderbird:** New Calendar → On the Network → iCalendar (ICS).

The link is read-only, so edits happen in SimplCalCon (or a full CalDAV client) and the subscription
refreshes on the client's own schedule (often hourly). The URL is a secret — anyone with it can read
the collection. Use **Reset** to rotate it (old links stop working) or **Disable** to revoke it.
Address books also offer a `.vcf` feed, though few clients subscribe to remote contact feeds.
For *two-way* Outlook sync on Windows desktop, use the third-party Outlook CalDav Synchronizer add-in
against `https://…/dav/` with an app password.

## Receiving invitations by email (inbound iMIP)

SimplCalCon can *send* invitation emails to external attendees (Admin → **Email (SMTP)**). To also
*receive* invitations/replies from external senders, enable one of two inbound paths (both are
off by default). SimplCalCon does not run a mail server, so mail must be handed to it.

**REST endpoint** — set `SimplCalCon:InboundEmail:ApiKey` to a secret, then have your mail system
(an MTA pipe, or an inbound-email webhook such as SendGrid Inbound Parse / Mailgun Routes /
Postmark) `POST` each raw message to `/api/inbound-imip` with the header `X-Inbound-Key: <secret>`.
The endpoint returns `404` until the key is configured.

**IMAP polling** — in Admin → **Email**, fill in the **Inbound (IMAP)** section (host, port, SSL,
username, password, folder) and tick *Poll a mailbox*, then set
`SimplCalCon:InboundEmail:PollerEnabled=true` (and optionally `PollSeconds`). The server polls each
configured mailbox for unseen mail and marks handled messages read. The IMAP password is stored
encrypted (Data Protection); the DP key ring is persisted in the database, so it survives restarts
with no extra operator setup (ADR 0083).

However it arrives, an incoming **REQUEST** appears in the recipient's invitations (bell badge), a
**REPLY** updates the organizer's event with the attendee's response, and a **CANCEL** removes the
event and notifies the attendee.
