# ADR 0013 — Data portability: import/export, takeout, server-to-server migration

## Status
Accepted (2026-07-23, spec interview)

## Context
The project description promises backup; onboarding requires getting existing data
out of Google/Nextcloud/iCloud/etc.; deleting a user must be able to hand them
their data (GDPR-friendly takeout).

## Decision
All three portability features are in scope:

1. **File import/export (Phase 1)**: upload an `.ics` or `.vcf` file into a
   collection (streamed, duplicate-UID handling: skip/replace chosen at import);
   download any calendar/address book as a single `.ics`/`.vcf` file via REST/web
   UI. Exports contain live objects only (no trash/revisions, ADR 0011).
2. **Takeout (Phase 2)**: one zip archive containing all collections of a user
   (self-service) or of a whole tenant (tenant admin) as standard `.ics`/`.vcf`
   files plus a manifest (collections, sharing metadata as JSON). Generated as a
   background job with a download link; also the artifact of delete-with-takeout
   (ADR 0006).
3. **Server-to-server migration (Phase 3)**: a built-in CalDAV/CardDAV *client*
   that, given a remote server URL + credentials, discovers and pulls all
   collections/objects into SimplCalCon (initial copy, re-runnable to pick up
   changes before cutover). This is a separate application service reusing the
   format libraries, not the server protocol layer.

## Consequences
- Import and migration must tolerate real-world dirty data (invalid iCal/vCard,
  duplicate UIDs, bad time zones): per-object error reporting, never
  all-or-nothing aborts.
- The migration client is genuine extra scope (DAV client semantics, remote-server
  quirks) — hence Phase 3, behind the core product.
- Takeout jobs need background-job infrastructure (also needed for trash purge and
  occurrence-window roll-over, ADR 0004/0011) — one shared job runner.
