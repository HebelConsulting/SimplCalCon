# ADR 0029 — Phase 1 data portability (as built)

## Status
Accepted (2026-07-23, Phase 1 implementation). Implements [ADR 0013](0013-data-portability.md).

## Context
`IObjectImportExport` (bulk .ics/.vcf per collection, per-object-resilient with Skip/Replace
conflict handling) already existed but was never surfaced. This unit adds the REST + UI
for per-collection import/export and a new account-wide **takeout** aimed at
**server-to-server migration** — so the archive is self-describing and re-importable, not
just a backup. No schema change.

## Decision

**Per-collection import/export (REST, ADR 0009).** On the calendar/address-book resources:
- `POST …/{id}/import` — **multipart** upload (`IFormFile` + `onConflict` = `skip`|`replace`,
  default skip) → `ImportResultResource` (`imported`/`skipped`/`failed`/`errors`). Multipart
  suits the browser file-picker. Needs `write-content`.
- `GET/HEAD …/{id}/export` — download the whole collection as `text/calendar` /
  `text/vcard` (`Content-Disposition` attachment). Needs `read`.

**Account takeout (`/api/takeout`), round-trip for migration.**
- `GET/HEAD /api/takeout` — an `application/zip` of the caller's **owned** collections:
  `calendars/<resourceName>.ics`, `addressbooks/<resourceName>.vcf`, and a
  **`manifest.json`** (version, exported-at, and per collection: type, display name,
  resource name, event/task support, file path).
- `POST /api/takeout` — upload such a ZIP; each manifest collection is recreated **always
  new** (fresh resource name, existing collections untouched) and its objects imported
  (Skip/Replace). Returns `TakeoutImportResource`
  (`collectionsCreated`/`imported`/`skipped`/`failed`/`errors`). A missing manifest or
  unreadable ZIP → **400 `INVALID_TAKEOUT`**. Requires a tenant (platform admins have no
  personal collections).

Manual A→B migration = download from A, upload to B. (Automated server-to-server pull is
still deferred.) Ingest generating fresh resource names means DAV URLs change across the
migration — acceptable; clients re-sync.

**Code.** `IAccountTakeout`/`AccountTakeout` (Infrastructure, `System.IO.Compression`) over
`IObjectImportExport` + the repository's list/create; `Api/Http/Portability` centralises
file reading, conflict parsing, `ImportOutcome` mapping, and file downloads. Import/export
actions live on `CalendarsController`/`AddressBooksController`; takeout on a new
`TakeoutController`.

**Web UI (ADR 0025).** Import (file picker + conflict select) and Export buttons on the
agenda and contacts views; a **Takeout** page (download / upload-to-import), linked from
Home. Authenticated downloads fetch the bytes with the bearer token and trigger a browser
save via a small `simplDownload` JS helper.

## Consequences
- **Verified**: 74 tests (25 unit + 49 integration). New `DataPortabilityTests` cover a
  calendar export→import round trip, the Skip conflict mode, a full takeout export→ingest
  (collections + objects recreated, both lists grow), and `INVALID_TAKEOUT` for a
  manifest-less archive.
- **Deferred**: automated server-to-server migration (credentialed pull between
  instances), merge-into-existing ingest, preserving original resource names/UIDs-as-URLs,
  a JSON export format, and streaming very large takeouts (currently buffered in memory —
  fine at the medium scale target, ADR 0014).
