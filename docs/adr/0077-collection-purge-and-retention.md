# ADR 0077 — Permanent purge + auto-retention for deleted collections

## Status

Accepted — implemented. Completes the collection lifecycle begun in ADR 0074 (type-the-name delete
guard) and ADR 0075 (self-service restore) — the "permanent purge from the UI" and "auto-retention
sweep for collections" items deferred there.

## Context

ADR 0075 made a soft-deleted calendar/address book restorable, but deleted collections then
**accumulate forever** with no way to reclaim the space or truly remove sensitive data — the owner had
no permanent-delete, and the retention sweep (ADR 0060) only purges trashed *objects*, not whole
collections. Two gaps: no manual hard-purge, and no automatic cleanup.

## Decision

Add permanent purge, keyed off the existing soft-delete state — **no schema change, no migration.**

**Manual purge (owner, from the UI):**
- `DELETE /api/{calendars|address-books}/deleted/{id}` — noun-based (deleting a member of the
  *deleted* set), owner-only, **If-Match-exempt** (it operates on an already-deleted collection, like the
  object-trash purge in ADR 0028); `404` if not found / not owned / not deleted (no leak).
- `IDavRepository.Purge{Calendar,AddressBook}Async(id, ownerId)` → `Remove` + `SaveChanges`. A single
  delete **cascades** to everything via the FK graph (objects → revisions/occurrences/attendees/photos,
  plus ACL entries, push subscriptions, per-user colours) — verified on both providers; EF enables
  SQLite FK enforcement, so `ON DELETE CASCADE` fires. No orphans, no child-table clearing needed.
- **UI:** a **🗑 "delete permanently"** action on each row of the pane's "Deleted (N)" section
  (ADR 0075) opens a **type-the-collection-name confirmation** — the exact ADR 0074 guard, since purge
  is irreversible (the confirm button stays disabled until the name matches). The pane raises `OnPurge`;
  the parent page owns the confirm modal + the API call, then refreshes the deleted list.

**Auto-retention sweep (operator, opt-in):**
- `IRetentionService.PurgeDeletedCollectionsBeforeAsync(cutoff, batchSize)` hard-purges collections
  soft-deleted before the cutoff, batched (object subtree deleted explicitly like the trash purge; small
  child tables via cascade), in a transaction.
- `RetentionSweepService` (ADR 0060) now runs if **either** trash **or** collection retention is enabled,
  draining each independently per cycle. New config key `SimplCalCon:Retention:DeletedCollectionRetentionDays`
  (**default 0 = keep forever / disabled**, matching `TrashRetentionDays`); reuses `SweepHours`/`BatchSize`.
  Destructive → strictly opt-in.

## Consequences

- The collection lifecycle is now complete and symmetric: guarded delete (0074) → recoverable
  soft-delete + restore (0075) → permanent purge, manual or auto (0077).
- Purge is irreversible and cascades all history — hence the type-to-confirm gate and owner-only +
  deleted-only scoping on the endpoint.
- No `ChangeSequence` bump on purge (clients already saw the tombstone at soft-delete time), consistent
  with object purge.

## Deferred

- Tenant-admin purge of another user's deleted collection (kept owner-only).
- OpenIddict/other token-table pruning (unrelated; tracked under ADR 0076).
- Live-object revision-history pruning (ADR 0060/0061 — separate unit).
