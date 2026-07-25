# ADR 0060 — Auto-retention trash purge

## Status

Accepted — implemented. Completes an ADR 0004/0011/0028 deferred item.

## Context

Objects soft-delete to trash and keep full revision history (ADR 0011/0028); purging is otherwise
only manual (per-object, or empty-trash per collection). Without a sweep, trashed objects accumulate
indefinitely. ADR 0028 deferred an "auto-retention purge job".

## Decision

An **opt-in** background sweep that permanently purges **trashed objects** past a retention window.

- **`IRetentionService.PurgeTrashedBeforeAsync(cutoff, batch)`** (Infrastructure): deletes up to
  `batch` objects with `IsDeleted && DeletedAt < cutoff` — oldest first — together with their
  `ObjectRevision` history, in one transaction. A **hard delete** like `IObjectStore.PurgeAsync`:
  **no change-sequence bump** (clients already saw the tombstone, per ADR 0028), across all
  collections/tenants.
- **`RetentionSweepService`** (`BackgroundService`): each cycle computes `now - TrashRetentionDays`
  and **drains** all eligible objects in batches, then sleeps. A failed cycle is logged and retried.
- **Opt-in / safe default.** Config `SimplCalCon:Retention` — `TrashRetentionDays` defaults to **0
  = keep forever (sweep disabled)**; an operator sets a window to enable. `SweepHours` (24) and
  `BatchSize` (500) tune cadence/throughput. Since the sweep permanently deletes data, nothing is
  purged out of the box.

**Scope:** trash only. Revision pruning of *live* objects (history trimming by age/keep-count) is
**not** included — all version history is retained.

No schema change (soft-delete + revisions already exist), no new dependency.

## Consequences

- Operators can cap how long trash lingers before permanent deletion; the default keeps everything,
  so existing deployments are unaffected until they opt in.
- Purge is irreversible (that's the point) — the retention window is the only guard, so a
  conservative default matters.

## Deferred

- Revision-history pruning (by age or keep-last-K) for live objects; a per-tenant retention policy;
  an admin-triggered "purge now" action.
