# DAV client acceptance matrix

The DAV surface's definition of done includes real native clients, which automated
tests can't fully cover (ADR 0003). Integration tests assert the protocol at the XML
level; this checklist tracks manual acceptance against the clients we commit to
supporting. Run it against a deployed instance (an app password per device).

Legend: ✅ verified · ⬜ not yet checked · ⚠️ works with caveat (note it).

## CardDAV (ADR 0021)

| Flow | iOS/macOS Contacts | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/carddav` | ⬜ | ⬜ | ⬜ |
| Discovers current-user-principal + addressbook-home-set | ⬜ | ⬜ | ⬜ |
| Default `contacts` address book appears | ⬜ | ⬜ | ⬜ |
| Create contact on device → appears on server | ⬜ | ⬜ | ⬜ |
| Edit contact → ETag/If-Match update, no conflict | ⬜ | ⬜ | ⬜ |
| Delete contact → removed on server | ⬜ | ⬜ | ⬜ |
| Change on server → syncs to device (sync-collection) | ⬜ | ⬜ | ⬜ |
| Delete on server → removed on device (tombstone) | ⬜ | ⬜ | ⬜ |
| Create a second address book (MKCOL) | ⬜ | ⬜ | ⬜ |

## CalDAV

_Pending the CalDAV unit._

## Notes / caveats

- `addressbook-query` filters are not yet evaluated server-side (returns all live
  objects with the requested props) — verify clients tolerate the superset.
- `address-data` returns the full card only (no partial retrieval).
