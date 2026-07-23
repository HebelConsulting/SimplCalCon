# ADR 0011 — Data safety: trash (soft-delete) + per-object version history

## Status
Accepted (2026-07-23, spec interview)

## Context
The classic sync catastrophe is a device wiping a whole calendar/address book via
legitimate-looking DAV deletes, or an edit destroying data. Backups alone make
recovery an operator task; users need self-service restore.

## Decision
- **Version history**: every write to an object creates a new immutable revision
  (blob + ETag + author principal + surface + timestamp). Prior revisions are
  browsable and restorable (restore = new revision with the old content) via
  REST/web UI. Pruning policy is tenant-configurable (Phase 3; default: keep all).
- **Trash**: deleting an object (either surface) marks it deleted and starts a
  retention window (default 30 days, tenant-configurable later). Deleting a whole
  collection trashes the collection. Within the window, users restore from trash
  via web UI/REST; afterwards a background job purges object + revisions.
- **DAV semantics preserved**: a trashed object is gone from the DAV surface — it
  vanishes from listings, `GET` returns 404, and sync-collection reports it as
  removed. Restore surfaces it as a new/changed object under sync. DAV clients
  never see trash or revisions.
- ETag/`If-Match` interplay: a `PUT` over a trashed UID is a create (new object,
  fresh history linked to the old one only via UID for audit purposes).

## Consequences
- Storage grows with edit frequency; the medium scale target (ADR 0014) makes this
  acceptable, and Phase 3 pruning policies bound it. Revision blobs are candidates
  for compression.
- Every DAV listing/sync/query must filter trashed objects — enforced structurally
  in the query layer (same pattern as tenant scoping, ADR 0006).
- Takeout/export (ADR 0013) exports live data only; revisions/trash are excluded
  from v1 export formats.
