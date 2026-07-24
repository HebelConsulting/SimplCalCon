# 0041 — Collection management: rename, delete, and import merge-by-name

## Status

Accepted (2026-07-24).

## Context

Three gaps around calendars / address books surfaced together:

1. No way to **rename** a collection.
2. **Delete** existed server-side (soft-delete, ADR 0023) but had no web UI.
3. A Google Calendar export lists **multiple calendars under the same `X-WR-CALNAME`**, so the
   per-file archive import (ADR 0040) created confusing same-named duplicates — and a double-submit
   (fixed by the import busy-lock) doubled everything.

## Decision

- **Rename** — `PUT /api/{calendars|address-books}/{id}` with `{ name }`. Owner-only (ADR 0023),
  `If-Match`. Updates the collection's `Name` via `IDavRepository.RenameCollectionAsync` (which
  bumps the concurrency token → new ETag). The **resource name / URL is unchanged** — only the
  display name. UI: a **Rename** ribbon button → a small modal.
- **Delete** — the existing owner-only **soft-delete** `DELETE` endpoint, now surfaced with a
  **Delete** ribbon button → a confirmation modal (names the collection; notes it's recoverable by
  an admin). Soft-delete hides the collection + its objects from listings but retains the data.
- **Import merge-by-name** — the separate-collections import (ADR 0040) gains a **`mergeByName`**
  flag (default **true**; a checkbox shown when "separate" is ticked): files that resolve to the
  same name (`X-WR-CALNAME` else file name) are imported into **one** collection instead of creating
  duplicates. `ImportArchiveToNewCollectionsAsync` keeps a name→collection map and reuses it.
- The awkward modal checkbox layout (label squeezed into a fixed left column) is fixed with a shared
  global **`.form-check`** class (the label is the flex row, text flows full-width).

## Consequences

- Calendars and address books can be renamed, deleted, and de-duplicated on import from the web UI —
  enough to clean up an accidental double-import.
- Delete stays **soft** (data retained, admin-recoverable); a hard purge is deferred.
- Rename is display-name only — existing DAV/REST URLs keep working.
- With `mergeByName` on, a Google export with two "Family" calendars yields one "Family"; turn it off
  to keep them separate.
