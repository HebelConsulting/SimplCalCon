# ADR 0020 — Phase 1 calendar/contact object store

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
ADR 0004 committed to hybrid blob-plus-indexed storage. This unit builds the storage
core: the collection/object model, the single write path, the parsers, and
import/export — the foundation the DAV surface and REST resource controllers sit on.

## Decision

**Model (all TPH).**
- **Collections** → `Calendar` (color, `SupportsEvents`/`SupportsTasks`, `TimeZoneId`)
  and `AddressBook`. Owned by a user, tenant-scoped, with a `ResourceName` unique per
  owner and a `ChangeSequence` backing the CTag.
- **Objects** → `CalendarObject` (VEVENT+VTODO: component type, summary, `DtStartUtc`/
  `DtEndUtc`, all-day, recurring) and `ContactObject` (FN, family/given, org, joined
  lowercased emails/phones). Base holds the **blob (source of truth)**, `Uid` and
  `ResourceName` (each unique per collection), tombstone state, `RevisionNumber`, and
  `ChangeNumber`.
- **ObjectRevisions** — every write (create/update/delete) appends an immutable
  revision (blob + ETag + operation + author). Full ADR 0011 history from the start
  (chosen over deferring).

**Datetimes are UTC.** New columns are UTC `DateTime`; a model-wide value converter
forces `Kind=Utc` on read/write. This also makes them orderable/range-queryable on
SQLite (unlike `DateTimeOffset`, ADR 0019). Existing `DateTimeOffset` columns are left
as-is (already UTC instants).

**Parsers (both MIT).** Ical.Net for iCalendar (recurrence/timezones; `CalDateTime.AsUtc`
for extraction), FolkerKinzel.VCards for vCard fields. UIDs are extracted at the line
level (robust across library versions); a vCard lacking a UID gets one injected before
`END:VCARD`, preserving the rest of the bytes.

**Write path (`IObjectStore`, one transaction).** parse → validate (UID present;
component allowed by the calendar) → store blob → extract fields → save (regenerates
the object ETag) → append the revision with that ETag → bump `Collection.ChangeSequence`
and set the object's `ChangeNumber` → commit. Deletion writes a tombstone + a `Deleted`
revision. The collection is loaded and modified on every write, so its **concurrency
token serializes concurrent writes to the same collection**, keeping the change
sequence strictly increasing (a losing writer gets `DbUpdateConcurrencyException`).

**Import/export (`IObjectImportExport`, ADR 0013).** Import splits a multi-object file
(calendar: re-serialized per UID with its VTIMEZONEs; vCard: at `BEGIN/END:VCARD`
boundaries) and runs each through the write path; per-object errors never abort the
batch. Export merges calendar objects into one VCALENDAR / concatenates vCards, ordered
by resource name (SQLite can't `ORDER BY` the DateTime columns).

## Consequences
- **Deferred:** occurrence-window index + RRULE/override expansion (needed for
  time-range REPORT/free-busy — the DAV unit); ACL sharing; trash retention/purge.
  Extraction currently captures the master component's start/end only.
- The revision's ETag equals the object's post-write token, which requires **two saves
  in the transaction** (the token is generated during `SaveChanges`).
- Contact email/phone search uses joined lowercased strings (substring match), not a
  normalized child table — adequate for autocomplete; revisit if structured queries
  are needed.
- The write path throws intent-named `ObjectStoreException` subtypes
  (`MalformedObjectException`, `CollectionNotFoundException`, `ComponentNotAllowedException`,
  `UidConflictException`); the Api boundary will map these to `ApiException`s in the
  DAV/REST units.
