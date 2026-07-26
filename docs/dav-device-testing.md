# Testing native clients on a developer machine (with the internal certificate)

How to point real CalDAV/CardDAV clients — Apple Calendar/Contacts, **DAVx⁵ (Android)**, iOS, Thunderbird,
Outlook CalDav Synchronizer — at a **local** SimplCalCon instance and get them syncing, including the
certificate handling that trips most people up. This is the reproducible workflow behind the
[client acceptance matrix](dav-client-matrix.md); per-client account fields live in
[`manual.md`](manual.md#connecting-native-calendar--contacts-clients-caldavcarddav).

## The core problem: HTTPS + a certificate the device trusts

- The demo runs on `http://localhost:9080` (web/REST/DAV) with a Caddy TLS proxy on `https://localhost`.
- **Same machine** (Thunderbird, macOS Calendar/Contacts): easy — `localhost` works.
- **Another device** (iOS, Android): must reach the machine by **LAN IP or hostname**, and Apple/iOS
  require **HTTPS with a trusted certificate**. Caddy issues one from its own *internal CA*, so the
  device must **trust that CA** first. (Android/DAVx⁵ can skip all of this — see below.)
- **Good news:** the DAV surface uses **relative** redirects/hrefs (ADR 0032), so a client that connects
  by IP/hostname stays there — there is no bounce back to `localhost`.

## 1. Bring the stack up so it serves your LAN host

Pick `LAN_HOST` — your machine's IP (`ipconfig getifaddr en0` on macOS) or, better, its `.local` mDNS
name (a hostname sidesteps the IP-SNI issue and iOS resolves `.local` via Bonjour). Then start with the
opt-in override (ADR 0032):

```bash
LAN_HOST=10.0.2.23 docker compose -f docker-compose.yaml -f docker-compose.lan.yaml up -d
```

The proxy now serves `https://localhost` **and** `https://<LAN_HOST>` from the same internal CA (kept in
the `caddy-data` volume, so it survives restarts — install it on the device once).

## 2. Create an app password

DAV auth uses a per-device **app password**, never the account password. In the web UI
(`http://localhost:9080`, sign in — demo: `admin@demo.local` / `ChangeMe-Demo-2026`) → **Configuration →
app passwords → create**. Use your **account email** as the username everywhere below.

## 3. Export Caddy's root CA

```bash
docker compose exec proxy cat /data/caddy/pki/authorities/local/root.crt > caddy-root.crt
```

`caddy-root.crt` is the CA to trust on each device. (It's stable across restarts thanks to the volume.)

## 4. Trust the CA per platform

- **macOS** (for local Apple clients): double-click `caddy-root.crt` → Keychain Access → find it →
  **Get Info → Trust → When using this certificate: Always Trust**. (Only needed for the same-machine
  Apple apps; `curl`/CLI can use `-k` or `DAV_SMOKE_STRICT_TLS`.)
- **Thunderbird** (its own store, ignores the OS): **Settings → Privacy & Security → Certificates →
  Manage Certificates → Authorities → Import** → `caddy-root.crt` → trust for websites.
- **iOS / iPadOS:** AirDrop or email `caddy-root.crt` to the device → **Settings → General → VPN &
  Device Management → install the profile** → then **Settings → General → About → Certificate Trust
  Settings → enable full trust** for the Caddy root. (Both steps are required.)
- **Android / DAVx⁵:** *usually unnecessary* — see the shortcut below. If you do want HTTPS on Android:
  **Settings → Security → Encryption & credentials → Install a certificate → CA certificate**.

## 5. Confirm the wire before touching a client

Run the smoke harness against the **exact host the device will use** — it does discovery + a
PUT/GET/REPORT/DELETE round-trip and isolates deployment/auth/TLS from client quirks:

```bash
scripts/dav-smoke.sh https://10.0.2.23 you@example.com "your-app-password"   # iOS/HTTPS path
scripts/dav-smoke.sh http://10.0.2.23:9080 you@example.com "your-app-password" # DAVx5/HTTP path
```

All green ⇒ a real client will connect with the same URL + credentials.

## 6. Connect the clients

- **DAVx⁵ (Android) — the easy one, no cert:** DAVx⁵ accepts plain HTTP over the LAN. *Add account →
  Login with URL and user name* → Base URL `http://<LAN_HOST>:9080/dav/`, username = email, password =
  app password. It discovers all calendars + address books. (No proxy, no certificate — the API binds
  `0.0.0.0:9080` directly.)
- **iOS / iPadOS:** *Settings → Calendar → Accounts → Add → Other → Add CalDAV Account*; Server =
  `<LAN_HOST>`, user = email, password = app password. Repeat for *Add CardDAV Account*. (Requires the
  trusted CA from step 4.)
- **Same-machine (Thunderbird, macOS):** use `https://localhost/dav/` — no LAN host needed.
- **Full per-client field tables:** [`manual.md`](manual.md#connecting-native-calendar--contacts-clients-caldavcarddav).

Quick alternative that needs **no certificate or LAN override** (handy for a one-off iOS test): a tunnel
— `cloudflared tunnel --url http://localhost:9080` — gives a public, already-trusted `https://…` URL to
point the client at. Stop it when done.

## 7. Watch it work (and verify delta sync)

Tail the request summary as the client syncs:

```bash
docker compose logs -f api | grep '/dav'
```

Healthy sync = `207`s (an initial `401` per path is the normal Basic-auth challenge). For a deep look —
e.g. to confirm a client uses **delta sync** (a `sync-collection` REPORT replaying its stored
`sync-token`, RFC 6578) rather than re-fetching everything — enable the DAV **wire trace** (full
request/response bodies, ADR 0033), then re-sync:

```bash
# add to the api service env (a throwaway compose override), recreate api, then trigger a client sync
Serilog__MinimumLevel__Override__SimplCalCon.Dav.Wire=Verbose
docker compose logs api | grep -A1 'sync-collection'   # a stored <sync-token> in the request = delta
```

> ⚠️ The wire trace logs **contact and calendar contents** — it's for local diagnosis only. Turn it
> back off (recreate the api without that env) when finished; never enable it in production.

Record outcomes in the [acceptance matrix](dav-client-matrix.md).

## 7b. Verify WebDAV-Push (DAVx⁵) — 🔜 the current next target

The LAN override enables **ephemeral VAPID keys** (`SimplCalCon__WebPush__AllowEphemeralKeys=true`), so
WebDAV-Push (ADR 0052) is on. Confirm it and run the push acceptance flow:

```bash
docker compose logs api | grep -i 'EPHEMERAL VAPID'   # startup line = push enabled (keys reset on restart)
```

Then, with DAVx⁵ connected over the LAN:
1. In DAVx⁵, ensure the account's sync uses **push** (it reads `push:transports`/`push:topic` from the
   collection PROPFIND automatically — no manual VAPID key needed).
2. Watch for DAVx⁵'s `push-register` and the server's fan-out (enable the wire trace to see the
   advertisement + register bodies):
   ```bash
   docker compose logs -f api | grep -iE 'push-register|/dav/push-subscriptions'
   ```
3. Edit an event/contact **from another client** (the web UI, or Thunderbird) and confirm DAVx⁵ syncs
   **within seconds, with no manual refresh**. Fill in the WebDAV-Push checklist in the acceptance matrix.

> Requires a DAVx⁵ device with Google Play Services (the push transport). Ephemeral keys reset on every
> api restart — any existing device subscription drops and DAVx⁵ re-registers on its next sync.

## 8. Tear down

```bash
docker compose down          # keep data (the caddy CA + DB volumes persist)
docker compose down -v       # also wipe volumes (next run mints a NEW CA → re-trust on devices)
```

## Gotchas

- **Bare-IP `LAN_HOST`** needs `default_sni` (already set in `deploy/Caddyfile.lan`): clients send no SNI
  for an IP literal (RFC 6066), so without it Caddy can't select the cert and the TLS handshake fails
  with an "internal error". A `.local` hostname avoids this entirely.
- **`down -v` regenerates the CA** — devices must re-trust the new `caddy-root.crt`. Plain `down` keeps it.
- **Firewall / same network:** the device must be on the same LAN, and the host must allow inbound `9080`
  (macOS may prompt) and `443`.
- **App password only** — the account password will not authenticate on `/dav`.
