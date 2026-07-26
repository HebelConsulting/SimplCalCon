# ADR 0078 — Mandatory backup before permanently purging a collection

## Status

Accepted — implemented. Adds a safety net to the permanent purge from ADR 0077.

## Context

ADR 0077 lets an owner permanently purge a soft-deleted calendar/address book — irreversible, cascading
away every event/contact and all history. The type-the-name guard (ADR 0074) confirms *intent*, but a
user can still destroy data they didn't realise they wanted. A cheap, strong safeguard is to force a
**backup download** of the collection before the purge can proceed.

The existing export (`GET …/{id}/export`) can't help: `IObjectImportExport.ExportAsync` gates on
`!collection.IsDeleted` and throws for a soft-deleted collection — the only ones that can be purged.

## Decision

Require a backup download in the purge modal before the destructive button unlocks — **safest ordering:
the backup is confirmed saved before anything is destroyed.**

- **Export a soft-deleted collection.** `IObjectImportExport.ExportAsync(id, includeDeletedCollection: true, …)`
  overload lifts the `!IsDeleted` collection gate (live objects only, as normal). New owner-only
  endpoints `GET/HEAD /api/{calendars|address-books}/deleted/{id}/export` stream the `.ics`/`.vcf`
  (`GetDeleted{Calendar,AddressBook}ByIdAsync` resolves + owner-checks the deleted collection; `404`
  otherwise). Read-only — no schema change.
- **Download-gated purge (UI).** The "delete permanently" modal now has a **"Download backup"** button;
  the destructive button is `disabled` until the user has **both** downloaded the backup **and** typed
  the collection's name (the ADR 0074 guard). The download reuses the `simplDownload` JS helper (ADR 0029)
  on the authenticated `ExportDeletedCollectionAsync` bytes, filename = the collection's name. The
  `purgeExported` flag resets each time the modal opens and after a purge.

## Consequences

- Permanent deletion now always leaves the user with a portable copy of the data — a real accident is
  recoverable from the download even though the server copy is gone.
- The gate is a **UI safeguard** (like the type-to-confirm): the purge API itself stays a plain `DELETE`,
  so a scripted caller isn't forced to export. Enforcing it server-side would need purge to consume a
  just-issued export token — deferred as overkill for the risk.
- The soft-deleted-collection export endpoint is also a useful primitive on its own (backup without
  restoring first).

## Deferred

- Server-side enforcement (export-token handshake) if a stricter guarantee is ever needed.
- Including trashed objects in the pre-purge backup (currently live objects only, standard export semantics).
- **Known gap (pre-existing, ADR 0075/0077):** when a user has *zero* live collections, the pane — and
  thus the "Deleted" section's restore/purge — is hidden behind the "none yet" message; deleting your
  only collection currently strands it. Track separately.
