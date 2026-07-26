# ADR 0075 — Self-service restore of deleted collections

## Status

Accepted — implemented. Completes the recovery story left open by ADR 0074 (type-the-name delete guard).

## Context

Deleting a whole calendar or address book is an owner-only **soft delete** (`Collection.IsDeleted` +
`DeletedAt`; child objects untouched — ADR 0004/0011/0041). The data survived, but there was **no
client-facing way to bring it back** — the delete modal said "can be undone by an administrator," which
in practice meant a hand-written SQL `UPDATE`. Per-object deletes already have self-service recovery via
the Trash (ADR 0028); collection-level deletes did not. ADR 0074 made accidental deletion harder; this
ADR makes an actual deletion recoverable without an operator.

## Decision

Expose the owner's soft-deleted collections and let them restore one, entirely over the existing
`IsDeleted` flag — **no schema change, no migration.**

**API (owner-only):**
- `GET/HEAD /api/{calendars|address-books}/deleted` → the caller's deleted collections
  (`ListDeleted{Calendars,AddressBooks}Async`, scoped `IsDeleted && OwnerId == caller`, most-recently-deleted
  first). `deleted` is a literal segment and never collides with the `{id:guid}`-constrained routes.
- `POST /api/{calendars|address-books}/{id}/restore` → flips `IsDeleted=false`, `DeletedAt=null`
  (`Restore{Calendar,AddressBook}Async`). **Owner-only and If-Match-exempt** — it acts on an
  already-deleted collection, mirroring the object-trash restore convention (ADR 0028); a non-owner or
  unknown id returns `404` (no existence leak). Restore can't hit a slug clash because the
  `(OwnerId, ResourceName)` unique index already spans deleted rows, so the row's slug is globally unique.
- `DeletedAt` is surfaced on `CalendarResource`/`AddressBookResource` (and the client DTOs); it's null
  for live collections.

**Web UI (ADR 0062 pane):** a collapsible **"Deleted (N) ▾"** footer in the reusable `CollectionsPane`
(hidden when there are none), each row showing the collection's colour + name and a **↺ restore**
action. Wired into both the Calendar and Contacts tabs: the deleted list loads alongside the live list
and refreshes after a delete (the just-deleted collection drops into the section immediately) and after
a restore. On restore the collection is re-checked so it reappears in the merged view at once, and the
view reloads + re-subscribes to live updates.

**Scope: restore only.** Permanent purge stays admin/manual — a deleted collection simply sits
recoverable. (Auto-retention sweeps trashed *objects*, ADR 0060, not whole collections.)

## Consequences

- A collection deleted by accident is one click from coming back, with all its entries and history
  intact — the "ask an administrator" dead-end is gone.
- The delete flow is now symmetric with object trash: guarded on the way out (ADR 0074), recoverable on
  the way back (this ADR).
- No hard-delete path for collections exists from the client, so deleted collections accumulate until an
  operator purges them — acceptable at the medium scale target (ADR 0014); revisit with a retention
  sweep for collections if it becomes a problem.

## Deferred

- Permanent purge of a deleted collection from the UI (and an auto-retention sweep for collections).
- Tenant-admin restore of another user's deleted collection (kept owner-only for now).
- Showing the entry count / original delete author in the deleted list.
