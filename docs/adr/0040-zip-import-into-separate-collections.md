# 0040 — Zip import into separate new collections (mimic a Google export's structure)

## Status

Accepted (2026-07-24).

## Context

ADR 0039 imports every entry of a zip into the **one** selected collection. But a Google Calendar
export is a zip with **one `.ics` per source calendar** ("Work", "Family", …), each already a
distinct calendar. Merging them all into a single calendar loses that structure; users want to
**recreate the original calendars** — one new calendar per file.

## Decision

The per-collection import endpoint gains an opt-in **`separateCollections`** form flag.

- When `separateCollections=true` **and** the upload is a zip, the request routes to
  `IObjectImportExport.ImportArchiveToNewCollectionsAsync`, which — mirroring the account-takeout
  ingest (ADR 0029) — **creates a fresh collection per matching entry** (a calendar per `.ics`, an
  address book per `.vcf`) and imports that file's objects into it. Existing collections are never
  touched.
- **Naming:** each new collection takes the file's **`X-WR-CALNAME`** (the display name Google/Apple
  embed in the VCALENDAR) when present, else the file name (minus extension). Resource names are a
  slug + GUID suffix so same-named files never collide.
- The result reports **`createdCollections`** alongside imported/skipped/failed
  (`ImportResultResource.CreatedCollections`).
- **UI:** the Import modal shows a checkbox — *"Create a separate calendar / address book for each
  file in the archive"* — only when the picked file is a `.zip`. After import the new collections
  appear in the tab's switcher (the client reloads the collection list).
- The route's `{id}` is just the collection the user was viewing; it's used only for the
  write-permission gate — the actual imports create new collections owned by the caller in their
  tenant (`InsufficientRightsException` if there is no tenant context).

## Consequences

- A Google export's calendar structure is recreated in one action; the same works for address books
  (a `.vcf` per book).
- **Simplifications:** naming keys on `X-WR-CALNAME`/filename only (no per-event calendar
  assignment); each new calendar is created supporting both events and tasks; upload size is bounded
  by the existing multipart/Kestrel limits (ADR 0039). Unchecked, the import still merges into the
  selected collection (ADR 0039 behaviour).
