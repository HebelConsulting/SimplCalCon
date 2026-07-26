# ADR 0082 — Structured (lossless) contact edit form

## Status

Accepted — implemented. Adds a field form for editing contacts alongside the raw-vCard editor (ADR 0036).

## Context

Editing a contact meant editing raw vCard text (ADR 0036) — powerful and lossless, but not friendly.
A structured field form is nicer, but the existing structured composer (`IObjectComposer.PutContactAsync`)
**rebuilds the card from scratch keeping only the UID** (ADR 0050 note) — so a naive form would silently
drop PHOTO, ADR, NOTE, BDAY, URL, email/phone TYPEs and any X-* extensions. The vCard blob is the source
of truth (ADR 0004), so a lossy field form would regress fidelity vs today's raw editing.

## Decision

A **rich field form** backed by a **lossless merge**, with the raw editor kept as an "Advanced" fallback.

- **Fields (v1):** formatted name, first/last, organisation, title, emails (multiple, with home/work
  type), phones (multiple, with mobile/home/work type), postal addresses (multiple), birthday, website,
  note. Everything else is preserved untouched.
- **Lossless merge** — a new `IContactCardComposer` (Infrastructure `ContactCardComposer`) works at the
  **vCard line level**: `Read` parses the blob into a structured `ContactCard`; `Merge` re-emits the card
  dropping only the modelled property lines (FN/N/ORG/TITLE/EMAIL/TEL/ADR/BDAY/URL/NOTE) and keeping every
  other logical line **verbatim** (PHOTO with its folding, X-*, IMPP, CATEGORIES, …), preserving UID and
  VERSION. A self-contained text merge (not FolkerKinzel's writer) keeps TYPE handling and preservation
  fully under our control; the stored blob still passes the normal `ContactObjectParser` validate-on-save,
  so a bad merge is rejected, never persisted.
- **API:** `GET/PUT /api/address-books/{id}/contacts/{cid}/card` (owner `read`/`write-content`, ETag +
  If-Match) — a structured sibling of the raw endpoints. GET returns the `ContactCard`; PUT merges it into
  the existing blob and writes through `IObjectStore` (so revisions/change-seq/extraction all fire).
- **UI:** the Contacts detail pane shows the structured card (read-only → **Edit** → repeatable
  email/phone/address rows with type dropdowns → **Save/Cancel**); the raw vCard editor moves under an
  **"Advanced: raw vCard"** disclosure. New-contact creation opens the form instead of raw text.

## Consequences

- Everyday editing is a clean form; nothing the form doesn't model is lost, so it's safe for real cards
  (photos, extensions) — and the raw editor is one click away for anything exotic.
- Editing a *modelled multi-value* field normalises its details: an email/phone TYPE outside
  home/work/mobile is rewritten to none, and re-emitted EMAIL/TEL/ADR use our canonical form. Properties
  the form doesn't model are untouched. (A card is only rewritten when the user saves the form.)
- Two structured contact write paths now exist: the old lossy `PutContactAsync` (rebuild) and the new
  lossless card merge. The web client uses the card path; the lossy one remains for simple REST callers.

## Deferred

- More field types (IMPP, categories, multiple orgs, nicknames) — preserved-but-not-editable for now.
- Structured **create** via a dedicated rich POST (today: create a stub, then edit the card).
- Richer TYPE fidelity (preserving arbitrary/verbatim TYPE tokens on edited values).
