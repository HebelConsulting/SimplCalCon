# ADR 0028 — Phase 1 trash & version history (as built)

## Status
Accepted (2026-07-23, Phase 1 implementation). Implements the user-facing half of
[ADR 0011](0011-soft-delete-and-version-history.md).

## Context
Storage already soft-deletes objects (tombstone `IsDeleted`/`DeletedAt`) and appends an
immutable `ObjectRevision` on every write (ADR 0004/0011), but nothing surfaced it:
deletes were irreversible from the user's view and history was invisible. This unit adds
the restore/purge/history surfaces. **Scope: objects only** (events/tasks + contacts);
calendars & address books keep their current delete behaviour.

**No schema change** — every field already existed. The one model touch is a new
`RevisionOperation.Restored` enum value, which is migration-free (the column is a
`string`-converted `HasMaxLength(20)`, no CHECK).

## Decision

**REST (ADR 0009), modelled as a `trash` sub-collection + a `revisions` sub-collection**
under both `…/events` and `…/contacts`:
- `GET  …/trash` — list trashed items (`read`); the resource carries `deletedAt`.
- `DELETE …/trash` — empty the trash (purge all).
- `DELETE …/trash/{id}` — purge one item permanently.
- `POST …/trash/{id}/restore` — bring one item back to the live collection.
- `GET  …/{id}/revisions` — version history, newest first (`read`).
- `POST …/{id}/revisions/{n}/restore` — reinstate a prior revision.

Writes need `write-content`. **Trash/restore/revision-restore are If-Match-exempt** — a
deliberate deviation from the ADR 0009 "If-Match on every mutation" rule: they are
recovery actions on already-deleted items (or an explicit "overwrite with this old
version" choice) invoked from management views that don't hold a live ETag. (Finer
gating — e.g. requiring the `Delete` right to purge — is a possible future tightening.)

**Semantics.**
- **Restore** re-materialises from a blob (the current tombstone blob, or revision `n`'s
  blob), clears the tombstone, and appends a `Restored` revision with a fresh change
  number, so DAV/REST sync report the re-appearance. A UID now taken by another live
  object makes restore fail (`UidConflictException`) — correct.
- **Purge** hard-deletes the object row + its revision history (FK cascade), with **no**
  change-sequence bump: DAV clients already saw the removal when it was trashed, so an
  old-sync-token client simply falls back to a full resync (RFC 6578) — standard.
- A missing revision → **404 `REVISION_NOT_FOUND`**.

**Write path.** `IObjectStore` gains `RestoreAsync`/`PurgeAsync`/`PurgeTrashAsync`;
`PutAsync` and `RestoreAsync` now share a `MaterializeAsync`/`CommitObjectAsync` pair (an
internal refactor — no behaviour change to Put) so extraction/tombstone/revision logic
lives once. Include-trashed reads (`ListTrashed…`, `Find…ByIdAsync`,
`ListObjectRevisionsAsync`) are on `IDavRepository` (the shared read port).

**Web UI (ADR 0025).** A `Trash` page per collection (restore / delete-forever / empty)
and a per-item `History` page (restore a version), linked from the agenda and contacts
views.

## Consequences
- **Verified**: 70 tests (25 unit + 45 integration). New `TrashHistoryTests` cover the
  trash→list→restore round trip, single purge (history gone), empty-trash, a
  Created→Updated→Restored history with a prior-revision restore, `REVISION_NOT_FOUND`,
  and the contact trash/restore path.
- **Deferred**: auto-retention (a background purge-after-N-days job), trashing whole
  collections, per-object diff/preview of a revision's content, and requiring the
  `Delete` right (vs `write-content`) for purge.
