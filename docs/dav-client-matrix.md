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

## CalDAV (ADR 0022)

| Flow | iOS/macOS Calendar | Android (DAVx⁵) | Thunderbird |
|---|---|---|---|
| Account setup via `/.well-known/caldav` | ⬜ | ⬜ | ⬜ |
| Discovers calendar-home-set | ⬜ | ⬜ | ⬜ |
| Default `calendar` appears | ⬜ | ⬜ | ⬜ |
| Create event on device → appears on server | ⬜ | ⬜ | ⬜ |
| Recurring event syncs and expands in views | ⬜ | ⬜ | ⬜ |
| Task (VTODO) create/sync (Reminders / Tasks.org) | ⬜ | ⬜ | ⬜ |
| Time-range refresh (calendar-query) returns the window | ⬜ | ⬜ | ⬜ |
| Edit → ETag/If-Match update | ⬜ | ⬜ | ⬜ |
| Delete on server → removed on device (sync-collection) | ⬜ | ⬜ | ⬜ |
| Create a second calendar (MKCALENDAR) | ⬜ | ⬜ | ⬜ |

## Notes / caveats

- `addressbook-query` / `calendar-query` filters are only partially evaluated
  server-side: address-book returns all live objects; calendar applies the
  `time-range` filter but not comp/prop/text filters — verify clients tolerate the
  superset.
- `address-data` / `calendar-data` return the full object (no partial retrieval or
  response-side recurrence expansion).
- Time-range expansion keys on occurrence start, so an event spanning into the window
  from before it can be missed (rare).
