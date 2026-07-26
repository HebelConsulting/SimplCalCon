# DAV client acceptance matrix

The DAV surface's definition of done includes real native clients, which automated
tests can't fully cover (ADR 0003). Integration tests assert the protocol at the XML
level; this checklist tracks manual acceptance against the clients we commit to
supporting. Run it against a deployed instance (an app password per device).

Legend: ✅ verified · ⬜ not yet checked · 🔜 next to verify · ⚠️ works with caveat (note it).

**Before checking a client**, run the wire smoke test against the deployed instance — it does what a
client's first connection does (discovery → home-sets → PUT/GET/REPORT/DELETE) and isolates
deployment/auth/TLS problems from client quirks:

```bash
scripts/dav-smoke.sh https://your-host you@example.com "app-password"
```

Per-client setup steps are in [`manual.md`](manual.md#connecting-native-calendar--contacts-clients-caldavcarddav);
the full **developer-machine testing workflow (with the internal certificate)** is in
[`dav-device-testing.md`](dav-device-testing.md).

**Microsoft Outlook** speaks no native CalDAV/CardDAV — see the separate
[Outlook gap analysis](outlook-gap-analysis.md); it interoperates via the CalDav Synchronizer add-in
(table below), the read-only ICS/VCF subscription feed (ADR 0069), or iMIP email invitations.

## CardDAV (ADR 0021)

| Flow | iOS/macOS Contacts | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/carddav` | ✅ macOS 15.7 | ✅ ¹ | ✅ ³ |
| Discovers current-user-principal + addressbook-home-set | ✅ | ✅ | ✅ |
| Default `contacts` address book appears | ✅ | ✅ | ✅ |
| Create contact on device → appears on server | ⬜ | ⬜ | ✅ ⁷ |
| Edit contact → ETag/If-Match update, no conflict | ⬜ | ⬜ | ✅ ⁷ |
| Delete contact → removed on server | ⬜ | ⬜ | ✅ ⁷ |
| Change on server → syncs to device (sync-collection) | ✅ | ✅ delta ² | ✅ delta ⁴ |
| Delete on server → removed on device (tombstone) | ⬜ | ⬜ | ✅ ⁸ |
| Create a second address book (MKCOL) | ⬜ | ⬜ | ⬜ |

## CalDAV (ADR 0022)

| Flow | iOS/macOS Calendar | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/caldav` | ✅ macOS 15.7 | ✅ ¹ | ✅ ³ |
| Discovers calendar-home-set | ✅ | ✅ | ✅ |
| Default `calendar` appears | ✅ | ✅ | ✅ |
| Create event on device → appears on server | ✅ | ⬜ | ✅ ⁵ |
| Recurring event syncs and expands in views | ⬜ | ⬜ | ⬜ |
| Task (VTODO) create/sync (Reminders / Tasks.org) | ⬜ | ⬜ | ⬜ |
| Time-range refresh (calendar-query) returns the window | ⬜ | ✅ | n/a ⁶ |
| Edit → ETag/If-Match update | ⬜ | ⬜ | ✅ ⁵ |
| Delete on server → removed on device (sync-collection) | ⬜ | ⬜ | ✅ ⁸ |
| Create a second calendar (MKCALENDAR) | ⬜ | ⬜ | ⬜ |

¹ DAVx⁵ (Android) verified against a deployed instance over the LAN via *Login with URL and user name*
at `http://<host>:9080/dav/` (Android accepts plain HTTP — no proxy or certificate needed). Account
setup and full discovery of both home-sets succeeded; every response was `207` (bar the standard
initial `401` Basic-auth challenge), with no `4xx`/`5xx` compatibility gaps.

² **Delta sync confirmed** via the DAV wire trace (ADR 0033): on a re-sync DAVx⁵ CTag-gates each
collection (a `getctag`/`sync-token` PROPFIND, skipping unchanged ones), then issues a
`sync-collection` REPORT carrying its **stored `sync-token`** (RFC 6578, e.g. `…/ns/sync/5`) and
`addressbook-multiget`/`calendar-multiget`s only the changed resources — an incremental delta, not a
full re-fetch. (The larger first-connect sync is a one-time full population.)

³ Thunderbird verified on this dev machine over `http://localhost:9080` (Thunderbird accepts plain
HTTP; no cert needed on-box). **Well-known auto-discovery confirmed** when pointed at the bare host:
`PROPFIND /.well-known/caldav → 301` and `/.well-known/carddav → 301`, after which Thunderbird follows
the redirect to `/dav/`, discovers the principal + both home-sets, and enumerates all owned/shared
collections — every response `207`, no `4xx`/`5xx`, no unhandled-DAV `405`/`501` warnings. (Given the
explicit `/dav/` collection URL instead, it connects straight there and skips well-known — expected.)
Thunderbird also probes `PUT /` during autoconfig, which the root correctly rejects with `405` without
affecting discovery.

⁴ **Delta sync confirmed** via the wire trace: Thunderbird's `sync-collection` REPORTs each replay its
**stored `sync-token`** (RFC 6578) and receive the incremented token in the response — incremental
delta, not a full re-fetch.

⁵ **Client→server event CRUD confirmed** via the wire trace: create = `PUT …ics → 201` (VEVENT body,
fresh `getetag` returned), edit = overwrite `PUT → 204`; a cross-calendar **move** (same UID `PUT →
201` in the target + `DELETE → 204` from the source) also round-tripped cleanly.

⁶ Thunderbird refreshes purely via `sync-collection` (RFC 6578) and issues **no `calendar-query`**, so
the time-range path isn't exercised by this client (it is by DAVx⁵ and Apple). Not applicable rather
than unverified.

⁷ **Client→server contact CRUD confirmed**: create = `PUT …vcf → 201`, edit = `PUT → 204`, delete =
`DELETE → 204`, all clean. **Server→device propagation** was also observed in the same session — a
contact edited via the web UI (`PUT /api/address-books/.../raw`) and an event edited via REST
(`PUT /api/calendars/.../events`) were both pulled by Thunderbird's next `sync-collection` REPORT.

⁸ **Delete-on-server → tombstone confirmed** via the wire trace: after a web-UI delete
(`DELETE /api/calendars/.../events → 204`), Thunderbird's `sync-collection` REPORT returned the
deleted resource's `<href>` with `<d:status>HTTP/1.1 404 Not Found</d:status>` and an advanced
`<d:sync-token>` (RFC 6578) — the tombstone the client uses to remove the item locally. The same
`404`-href tombstone was delivered for a deleted address-book object (`…vcf`).

## Outlook — CalDav Synchronizer add-in (classic Windows)

Two-way sync via the third-party add-in against the standard `/dav/` surface (ADR 0068 gap analysis).

| Flow | Outlook (CalDav Synchronizer) |
|---|---|
| Profile connects to `https://…/dav/` with email + app password | ⬜ |
| "Test or discover settings" lists calendars + address books | ⬜ |
| Event/contact created in Outlook → appears on server | ⬜ |
| Server change → syncs into Outlook | ⬜ |
| Two-way edit → no duplicate/conflict | ⬜ |

## Notes / caveats

- **macOS Contacts requires an `OPTIONS`/`PROPFIND` handler on the bare server root**
  (`/`), not just under `/dav`. During setup it probes `OPTIONS /` (RFC 6764 §6) and
  requires a `DAV: …addressbook…` header, then may `PROPFIND /` for
  `current-user-principal`; a `405` there makes it silently discard the account (no
  sync, absent from "Default Account") even though well-known discovery otherwise
  succeeds. macOS Calendar does **not** make this root probe, so CalDAV worked without
  it. Served by `CardDavServiceController` (`~/` OPTIONS + PROPFIND). Verified end to
  end on macOS 15.7 via reverse-proxy body tracing (full sync incl. multiget).
- `addressbook-query` / `calendar-query` filters are evaluated server-side, including
  `text-match`, `is-not-defined`, and `param-filter` (ADR 0043/0054). Nested `<comp>`
  selection is honored exactly, to any depth (ADR 0073).
- `address-data` / `calendar-data` honor a requested `<comp>`/`<prop>` subset, and
  `calendar-data` supports `expand` (one VEVENT per occurrence) — ADR 0054.
  `limit-recurrence-set` is honored (master + only in-range overrides, ADR 0068); the
  `<comp>`/`<prop>` subset (+ `expand`/`limit`) is honored on `sync-collection` too (ADR 0070).
- Time-range matching is **true RFC 4791 interval overlap** (ADR 0067): an event that
  started before a client's visible window but runs into it is returned (a look-back by
  the event's own duration catches spanning occurrences). Recurring events also now
  contribute their real duration to free/busy and to `calendar-data expand`.

## WebDAV-Push (ADR 0052) — ✅ server-side verified end-to-end

Moves DAVx⁵ from *polling* to *pushed* sync. **The whole server pipeline is verified against a real
device** — advertise, register, encrypt, and deliver to the push service. The only unproven link is the
device actually waking, which is a device push-app/battery-config matter, not a server one.

| Flow | DAVx⁵ (Android) |
|---|---|
| Collection PROPFIND advertises `push:transports` / `push:topic` | ✅ ³ |
| DAVx⁵ `POST`s a `push-register` → `204` (+ `Location` + `Expires`) | ✅ ³ |
| Server change → encrypted `push-message` (topic + sync-token) delivered to the push service | ✅ ⁴ |
| DAVx⁵ wakes and pulls the change via `sync-collection` within seconds | ⚠️ ⁴ |
| Unregister on account removal → `DELETE /dav/push-subscriptions/{id}` | 🔜 |

³ **Verified over the LAN** (ephemeral VAPID, `docker-compose.lan.yaml`). DAVx⁵ delivers push over
**UnifiedPush**, so the device needs a distributor app — tested with **ntfy**. With ntfy installed,
DAVx⁵ read the push advertisement and `POST`ed a `push-register` per collection (`204`); **8
subscriptions were stored** in `PushSubscriptions` (each an `https://ntfy.sh/up…?up=1` endpoint + p256dh
key + auth-secret + expiry), no `4xx`/`5xx`. Without a UnifiedPush distributor DAVx⁵ silently polls and
never registers.

⁴ **Server fan-out verified end-to-end.** After a change (made from the web UI), the server's encrypted
`push-message`s were observed **cached at the ntfy topic** (`GET https://ntfy.sh/<topic>/json?poll=1` —
base64 RFC 8291 aes128gcm ciphertext, one per change), with **no delivery error** logged. So the
notifier → topic+sync-token → VAPID-signed encryption → push-service delivery chain is proven. The
device did **not** auto-sync in the test — the failure was the **ntfy client couldn't hold its WebSocket
to ntfy.sh** (*"Websocket not supported…"*), so messages reached ntfy but never the phone. That's an
ntfy-client/network hop **outside SimplCalCon** (check ntfy's default server + network WebSocket
blocking + app version; or self-host ntfy). Also seen: Android **battery optimization** killing ntfy's
background socket. **Testing gotcha:** ephemeral VAPID keys rotate on every api restart, invalidating
existing device subscriptions — re-sync DAVx⁵ to re-register after any restart (a persistent VAPID pair
avoids this — ADR 0052). Troubleshooting: `manual.md` → "Instant sync with WebDAV-Push" and
`dav-device-testing.md` §7b.

- **What's built:** the server implements the bitfire WebDAV-Push draft
  (`https://bitfire.at/webdav-push`) over Web Push — collections advertise
  `push:transports`/`push:topic`, clients `POST` a `push-register` (→ `204` + `Location` + `Expires`),
  and every change fans out an encrypted `push-message` (topic + `{DAV:}sync-token`) so the client then
  pulls via `sync-collection`. Automated tests cover advertisement, register/unregister, and change
  fan-out with a capturing sender; the real-device server fan-out is confirmed via the ntfy topic (⁴).
- **Remaining:** only the on-device **wake** (ntfy → DAVx⁵), which is device push-app/battery config.
  Setup, the ntfy verification method, and the battery-optimization note are in
  [`manual.md`](manual.md#instant-sync-with-webdav-push-davx--ntfy) and
  [`dav-device-testing.md`](dav-device-testing.md) §7b.
- **Apple Calendar/Contacts do not use WebDAV-Push** (they use proprietary APNs push), so they are
  unaffected and keep polling.
