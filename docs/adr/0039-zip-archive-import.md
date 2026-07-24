# 0039 — Zip-archive import (multi-file .ics/.vcf, e.g. a Google export)

## Status

Accepted (2026-07-24).

## Context

Google Calendar's export (Settings → Import & export → Export, and Google Takeout) produces a
**`.zip` containing one `.ics` file per calendar** — often nested under a folder
(`Takeout/Calendar/*.ics`) alongside unrelated files. The per-collection import (ADR 0013/0029)
accepted only a **single** `.ics`/`.vcf` document, so importing a Google export meant unzipping by
hand and importing each file one at a time.

## Decision

The existing per-collection import (`POST /api/{calendars|address-books}/{id}/import`) **auto-detects
a zip** and fans out over its entries — no new endpoint, no new button.

- **Detection** (`Portability.IsZip`): by filename (`.zip`), content type (`application/zip`), or
  the `PK\x03\x04` magic bytes.
- **Fan-out** (`IObjectImportExport.ImportArchiveAsync`, Infrastructure): open the archive, take
  every entry whose file name ends with the collection's extension — **`.ics` for a calendar, `.vcf`
  for an address book** — recursively (folder nesting is fine; directory entries and unrelated files
  like `archive_browser.html` are ignored), and run each through the **existing** single-document
  `ImportAsync`. Outcomes are aggregated (imported/skipped/failed summed, errors concatenated). A
  single `.ics` entry may itself hold many `VEVENT`s — the import path already splits by UID.
- The **blob → validate → extract** write path, conflict mode (`skip`/`replace`), and ACL checks are
  entirely unchanged — the zip layer only chooses what text to feed in.
- A **corrupt archive** (`InvalidDataException`) maps to **400**; an archive with no matching entries
  returns a normal outcome (0 imported) with an explanatory error line.
- The client Import modals now accept `.ics,.zip` (Calendar) and `.vcf,.zip` (Contacts).

## Consequences

- One Import action handles a single file **or** a whole Google export zip, for both calendars and
  address books.
- **Simplifications:** no nested-zip recursion (only `.ics`/`.vcf` entries); no per-entry manifest
  (unlike account **takeout**, ADR 0029, which is our own self-describing format — this reads a
  *foreign* archive into one existing collection). Upload size is bounded by the existing multipart /
  Kestrel request limits, so a very large Takeout archive could exceed them — streaming large
  archives is deferred.
