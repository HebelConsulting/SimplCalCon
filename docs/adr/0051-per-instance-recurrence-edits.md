# ADR 0051 — Per-instance recurrence edits + monthly Nth-weekday

## Status

Accepted — implemented. Extends ADR 0050.

## Context

ADR 0050 shipped whole-series recurrence editing and grid expansion, deferring the two hardest
pieces: **per-instance edits** ("this occurrence only" / "this and following") and **monthly
"Nth weekday"** rules. Both are common in real calendars (cancel one standup; move one meeting;
"the 2nd Tuesday of each month"). Neither needs a schema change — overrides/EXDATE live in the
object blob (which already holds a master VEVENT plus zero+ `RECURRENCE-ID` overrides; the parser
picks the master), and the Nth-weekday rule is just a wider RRULE stored in the existing
`RecurrenceRule` column.

## Decision

### Monthly Nth-weekday (structured editor)

Extend `Recurrence` with `ByMonthDay` and widen `RecurrenceRule.TryParse`/`Format`: a `MONTHLY`
rule may now be **"on day N"** (`BYMONTHDAY=15`) or **a single ordinal weekday**
(`BYDAY=2TU`, `BYDAY=-1FR`; ordinals 1–4 and -1). The editor shows a monthly mode selector
("Day of the month" / "A weekday of the month"). Multiple ordinals, 5th-weekday, `BYSETPOS`, etc.
remain outside the subset (round-trip as a read-only custom rule, per ADR 0050).

### Per-instance edits

Three scopes, chosen from a **"This event / This and following / All events"** prompt shown when
Edit or Delete is invoked on a recurring **occurrence** (a grid item, which carries its
`RECURRENCE-ID`; list rows edit the whole series):

- **This event** — *edit*: a `RECURRENCE-ID` override VEVENT with the edited fields; *delete*: an
  `EXDATE` on the master.
- **This and following** — *edit*: end the old series just before the occurrence (`UNTIL`) and
  create a **new series** (fresh UID) from the edited fields + the recurrence set in the editor;
  *delete*: end the series just before the occurrence.
- **All events** — the plain series update/delete (ADR 0050).

**Server.** `IRecurrenceEditor` (Infrastructure) applies the RFC 5545 transforms via Ical.Net
(`CalendarObjectParser.ExcludeOccurrence` / `SetOccurrenceOverride` / `TruncateSeriesBefore`) and
writes through `IObjectStore` (revision + ETag + change-sequence bump); the indexed row keeps
reflecting the master. Routes: `PUT`/`DELETE /api/calendars/{cid}/events/{id}/occurrences/{recurrenceId}?scope=this|following`
(recurrence-id is the occurrence's UTC slot in basic form `yyyyMMddTHHmmssZ`; a bad value → 400
`INVALID_RECURRENCE_ID`). Expansion now carries each occurrence's `RECURRENCE-ID` **and its
effective summary/location** (an overridden instance shows its own, sourced from the occurrence's
component), so the grid renders overrides correctly and a re-edit targets the right slot.

**Client.** The scope chooser modal; the editor hides the repeat controls in "this event" mode;
`ApiClient.UpdateOccurrenceAsync`/`DeleteOccurrenceAsync`; a Delete button on the event detail.

## Consequences

- Users can cancel/move/edit a single occurrence, or split "from here on", entirely from the web UI;
  changes sync to native CalDAV clients (standard EXDATE / RECURRENCE-ID / UNTIL in the blob).
- No schema change, no new dependency.

## Simplifications / deferred

- Per-instance edits **do not send iTIP** (scheduling on overrides is deferred); they're tenant-local
  blob edits like the rest of ADR 0050.
- "This and following" **edit** defines the going-forward rule from the editor (not copied from the
  master), so a master `COUNT` is not carried into the tail — the user sets the new rule.
- Re-editing an already-overridden occurrence maps its shown start back to the original slot via the
  override's `RECURRENCE-ID`; deeply nested override chains are not specially handled.
