# ADR 0062 — Collections pane, colours, and merged views

## Status

Accepted — implemented.

## Context

The Calendar and Contacts tabs each showed **one** collection at a time, chosen from a ribbon
dropdown. There was no way to see several calendars/address books together, and no per-collection
colour. We want a left pane listing the collections, each with a visibility checkbox (a filter), a
colour swatch/picker, and its name — with the shown tabs rendering the **union** of the checked
collections, colour-coded. This obsoletes the dropdown.

## Decision

### Colour storage

`Collection` (the TPH base for `Calendar` + `AddressBook`) carries a nullable **`Color`** hex string.
The column already existed but was mapped to `Calendar` only; it moves to the base so address books
get it too — a model-only move (the physical column is unchanged, so the migration is empty). Colour
is **collection-level and owner-set**: the existing owner-only `PUT /api/{calendars|address-books}/{id}`
(If-Match) is extended from `{name}` to `{name, color}` (`CollectionUpdateRequest`, colour validated
`#RRGGBB`/`#RRGGBBAA`), and `color` is added to the calendar/address-book resources. Sharees see the
colour read-only. A per-*user* colour override would need its own table and is out of scope; native
DAV clients keep their own colours (no `calendar-color` sync — REST/UI only).

### Client

- **`CollectionsPane`** (reusable): per-collection row = visibility **checkbox** + colour **swatch**
  (a native `<input type="color">` overlay, disabled for shared collections) + **name** (clicking it
  makes the collection *active*). Raises `OnFilterChanged` / `OnActiveChanged` / `OnColorChanged`.
- **Merged views:** `CalendarView` and `Contacts` fetch entries from **every checked collection** and
  render the union, each entry tagged with its owning collection's name + colour (`CollectionColors.For`
  — the stored colour, else a stable palette colour from the id). The grid tints event chips; the list
  views gain a **colour column (1st)** and a **collection column (2nd)**, both filterable, collection
  sortable.
- **Active collection** is the target for New/Import/Export/Rename/Delete/Share/Trash; per-entry
  actions (edit/delete/move/split/history, raw vCard, photo) target the entry's **own** collection, not
  the active one (an event/contact from a non-active collection still edits correctly). Bulk
  move/delete groups the selection by source collection for the per-collection bulk API.
- **Persistence:** the checked set + active id are stored in `localStorage` per tab
  (`simplcal.{calendar,contacts}.{checked,active}`); default is all-checked, first active. The
  `/calendars/{id?}` and `/address-books/{id?}` routes still deep-link (activate + show that one).
- Live updates (ADR 0049) subscribe to **all** checked collections and reload the merged view on any
  of them changing. The ribbon dropdown is removed.

## Consequences

- Users see and work across multiple calendars/address books at once, colour-coded; the pane replaces
  the dropdown as the switcher and the filter.
- Colour is a shared collection property; a sharee can't recolour someone else's collection.
- The list adds two columns; column widths persist via the existing resizer.

## Deferred

- Per-user colour overrides; syncing colour to/from CalDAV `calendar-color`; a "select all / none"
  shortcut in the pane; drag-to-reorder collections.
