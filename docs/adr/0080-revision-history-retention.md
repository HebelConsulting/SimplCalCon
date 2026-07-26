# ADR 0080 — Revision-history retention

## Status

Accepted — implemented. Closes the "revision-history pruning not included" deferral from ADR 0060/0061,
completing the retention story (trash objects 0060, deleted collections 0077, revisions here).

## Context

Every write appends an immutable `ObjectRevision` (full-blob copy) for the full version history behind
the History page and restore-to-revision (ADR 0011/0028). For a frequently-edited object this history
grows **unbounded** — the only pruning today is wholesale deletion when the *object* itself is purged
(trash retention / collection purge). Long-lived, churny objects accumulate revisions forever.

## Decision

Add an **opt-in** revision-history prune to the existing `RetentionSweepService`, keyed on **both age and
a minimum count** (the chosen policy): a revision is pruned iff it is **older than the cutoff** *and*
falls **outside the most-recent `keepMinimum`** per object.

- Config `SimplCalCon:Retention`: **`RevisionRetentionDays`** (0 = keep all / disabled) +
  **`MaxRevisionsPerObject`** (the floor). `keepMinimum = max(1, MaxRevisionsPerObject)`, so the **newest
  revision always survives** regardless of age — History/restore is never emptied. **Opt-in** (default 0),
  consistent with the other retention sweeps, since it destroys recoverable history.
- Prune predicate per object: `CreatedAt < cutoff && RevisionNumber <= object.RevisionNumber - keepMinimum`.
  `CollectionObject.RevisionNumber` is a per-object monotonic counter (contiguous, last+1), so
  "outside the last N" is a **plain comparison off the object's counter** — no correlated `MAX` subquery
  (which SQLite's `ExecuteDelete` can't translate).
- **Provider-safe two-step** (`IRetentionService.PruneRevisionsAsync`, mirroring the existing prunes):
  select a batch of objects that still have prunable revisions (a correlated `EXISTS` in a *SELECT*,
  which both providers translate), then a plain `ExecuteDelete` per object. Batched/drained by the sweep;
  runs if trash **or** collection **or** revision retention is enabled. **No schema change.**

## Consequences

- Operators can bound history growth by age with a safety floor (e.g. "prune revisions older than a year,
  but always keep the last 50"), and the current state is never at risk — it lives on `CollectionObject.Blob`,
  not on any revision row.
- Restoring to a **pruned** revision returns `404 REVISION_NOT_FOUND` (expected — that version is gone);
  the History list simply stops at the retained window.
- Off by default: existing deployments keep full history until an operator opts in.

## Deferred

- A UI/REST surface to configure or trigger revision retention (operator config only for now).
- Per-collection or per-tenant retention overrides (single global policy today).
