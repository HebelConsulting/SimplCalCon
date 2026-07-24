# ADR 0036 — Contacts master-detail + raw vCard editing

## Status
Accepted (2026-07-24). Reshapes the Contacts tab from [ADR 0034](0034-web-client-shell-redesign.md).

## Context
The Contacts tab was a flat list with inline add/import forms. The requested shape: a
**master-detail** view (sortable/selectable list on the left; a split detail on the right —
the card's photo on top, the **raw vCard** below with in-place edit), and the **New address
book** + **Import** actions moved into **ribbon buttons that open modals**. Editing the card
verbatim needs the vCard text, which the structured REST surface (ADR 0009) doesn't expose.

## Decision

**Raw vCard endpoints (`ContactsController`).** `GET /api/address-books/{id}/contacts/{cid}/raw`
returns the card `text/vcard` + ETag; `PUT …/raw` (If-Match) stores the edited text through
the **same `IObjectStore` validate-and-extract write path** as any object write — so the
indexed fields stay in sync and a malformed card is rejected (`415`, or `409` on a UID
clash). This is a deliberate, narrow exception to "structured JSON only" (ADR 0009): the
vCard *is* the source of truth (ADR 0004), and power users edit it directly.

**Client (Blazor).**
- A reusable **`Modal`** component; **New address book** and **Import** are ribbon buttons
  opening modals (removed from Overview / the page body). **New contact** creates a stub and
  drops straight into edit.
- **Master-detail:** left = a `<table>` with **sortable** headers (Name · Organization ·
  Email · Phone · **Photo**; click to sort/toggle) and **selectable** rows, plus a
  **filter row** — a per-column text box for the first four and a **checkbox** on the Photo
  column ("only with photos"). Filtering + sorting are client-side over the loaded list.
  Right = a split pane — the **photo** (top) parsed from the card by `VCardPhoto` (base64
  `PHOTO;ENCODING=b` → `data:` URL, or a `data:`/URL value as-is; RFC 6350 unfolding) and
  the **raw vCard** (bottom) in a read-only `<textarea>` with **Edit** → editable +
  **Save**/**Cancel**.
- **Validate on save.** Save PUTs the raw text with the fetched ETag (If-Match). The server
  parses it through `IObjectStore` and **only persists if it's a valid vCard** — otherwise it
  rolls back (nothing stored) and returns a clear **`400 INVALID_VCARD`** (or `409
  VCARD_UID_CONFLICT`). The client shows the reason and **keeps the editor open with the
  edits intact** (a quick client-side `BEGIN/END:VCARD` check avoids an obvious round-trip);
  on success it reloads so the columns reflect the edit.
- **`HasPhoto`** is a computed flag on `ContactResource` (a PHOTO-property regex over the
  already-loaded blob — no schema change, no extra query) that drives the Photo column + the
  "only with photos" filter.

## Consequences
- The card is editable losslessly (fields the structured DTO doesn't model — e.g. the
  embedded PHOTO — survive an edit, unlike a field-form round-trip).
- `VCardPhoto` is best-effort display parsing (common encodings); an unrecognized PHOTO just
  shows "no photo".

**Layout.** The list and detail panes are separated by a **draggable divider** (`splitter.js`
sets the list's flex-basis; clamped), and the table's **columns are individually resizable**
(`columnResize.js` adds a grip to each header's right edge → sets that column's width;
`table-layout: fixed` + `min-width: 100%` so the initial fit has no horizontal scrollbar —
also fixing the last row being clipped — and widening a column grows the table so the pane
scrolls). The shell's `.content` **no longer hard-caps width at 64rem** (that capped the
whole split); text-heavy pages opt into a reading width via `.readable`, wide pages
(Contacts) use the full window. External photo URLs (Google) use
`referrerpolicy="no-referrer"` because those hosts reject a cross-origin `Referer`.

## Deferred
Multi-select / bulk actions; structured field editor alongside the raw one; inline photo
upload into the card; column resizing; server-side sort/paging for very large books.
