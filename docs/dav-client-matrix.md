# DAV client acceptance matrix

The DAV surface's definition of done includes real native clients, which automated
tests can't fully cover (ADR 0003). Integration tests assert the protocol at the XML
level; this checklist tracks manual acceptance against the clients we commit to
supporting. Run it against a deployed instance (an app password per device).

Legend: ✅ verified · ⬜ not yet checked · ⚠️ works with caveat (note it).

## CardDAV (ADR 0021)

| Flow | iOS/macOS Contacts | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/carddav` | ✅ macOS 15.7 | ⬜ | ⬜ |
| Discovers current-user-principal + addressbook-home-set | ✅ | ⬜ | ⬜ |
| Default `contacts` address book appears | ✅ | ⬜ | ⬜ |
| Create contact on device → appears on server | ⬜ | ⬜ | ⬜ |
| Edit contact → ETag/If-Match update, no conflict | ⬜ | ⬜ | ⬜ |
| Delete contact → removed on server | ⬜ | ⬜ | ⬜ |
| Change on server → syncs to device (sync-collection) | ✅ | ⬜ | ⬜ |
| Delete on server → removed on device (tombstone) | ⬜ | ⬜ | ⬜ |
| Create a second address book (MKCOL) | ⬜ | ⬜ | ⬜ |

## CalDAV (ADR 0022)

| Flow | iOS/macOS Calendar | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/caldav` | ✅ macOS 15.7 | ⬜ | ⬜ |
| Discovers calendar-home-set | ✅ | ⬜ | ⬜ |
| Default `calendar` appears | ✅ | ⬜ | ⬜ |
| Create event on device → appears on server | ✅ | ⬜ | ⬜ |
| Recurring event syncs and expands in views | ⬜ | ⬜ | ⬜ |
| Task (VTODO) create/sync (Reminders / Tasks.org) | ⬜ | ⬜ | ⬜ |
| Time-range refresh (calendar-query) returns the window | ⬜ | ⬜ | ⬜ |
| Edit → ETag/If-Match update | ⬜ | ⬜ | ⬜ |
| Delete on server → removed on device (sync-collection) | ⬜ | ⬜ | ⬜ |
| Create a second calendar (MKCALENDAR) | ⬜ | ⬜ | ⬜ |

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
  `text-match`, `is-not-defined`, and `param-filter` (ADR 0043/0054). Deep `allcomp`
  nesting is approximated.
- `address-data` / `calendar-data` honor a requested `<comp>`/`<prop>` subset, and
  `calendar-data` supports `expand` (one VEVENT per occurrence) — ADR 0054.
  `limit-recurrence-set` is honored (master + only in-range overrides, ADR 0068); partial
  data on `sync-collection` is not implemented.
- Time-range matching is **true RFC 4791 interval overlap** (ADR 0067): an event that
  started before a client's visible window but runs into it is returned (a look-back by
  the event's own duration catches spanning occurrences). Recurring events also now
  contribute their real duration to free/busy and to `calendar-data expand`.

## WebDAV-Push (ADR 0052) — pending manual acceptance

- **DAVx5 (Android)** is the target client. The server implements the bitfire
  WebDAV-Push draft (`https://bitfire.at/webdav-push`) over Web Push: collections
  advertise `push:transports`/`push:topic`, clients `POST` a `push-register`
  (→ `204` + `Location` + `Expires`) and receive an encrypted `push-message`
  (topic + sync-token) on every change, then pull via `sync-collection`.
- **Not yet verified against a real device.** Automated tests cover the wire pieces
  (advertisement, register/unregister, change fan-out) with a capturing sender; the
  RFC 8291 aes128gcm encryption + VAPID + real push-service delivery need a DAVx5
  device and a live push endpoint. To test: run with a configured (or dev-ephemeral)
  VAPID pair, add the account in DAVx5, confirm it registers, and edit an event/contact
  from another client — DAVx5 should sync within seconds without a manual refresh.
- **Apple Calendar/Contacts do not use WebDAV-Push** (they use proprietary APNs push),
  so they are unaffected and keep polling.
