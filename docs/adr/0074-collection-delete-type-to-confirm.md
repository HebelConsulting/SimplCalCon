# ADR 0074 — Type-the-name confirmation for collection deletes

## Status

Accepted — implemented (web UI). Companion self-service **collection restore** is planned as a
follow-up unit (deleted collections are already soft-deleted, so nothing is lost in the meantime).

## Context

Deleting an entire calendar or address book (ADR 0041/0042) is an owner-only action that removes the
whole collection and all its entries from every surface (web UI + DAV clients). Server-side it is a
**soft delete** (`Collection.IsDeleted = true` + `DeletedAt`; child objects untouched — ADR 0004/0011),
so the data survives, **but there is no client-facing way to restore it** — the delete modal only says
"can be undone by an administrator," which in practice means a manual SQL flip. The original delete UI
was a single confirm modal with an immediately-clickable **Delete** button, so one stray click
(followed by a reflexive "confirm") wiped a populated calendar. This actually happened in testing.

Per-entry deletes are lower-risk: they go to the per-object Trash (ADR 0028) with a self-service
Restore. The gap is specifically the **collection-level** delete, which has no self-service recovery.

## Decision

Gate the two collection-delete modals (Calendar and Contacts ribbon → **Delete**) behind a
**type-the-collection-name confirmation**, GitHub-style:

- The modal shows the collection's name and asks the user to type it. The destructive **Delete** button
  is **`disabled` until the typed text matches the name exactly** — trimmed on both sides,
  **case-sensitive** (`StringComparison.Ordinal`). No accidental fire path remains.
- Matching is a pure computed property (`DeleteConfirmed`) per page; the input is `@bind:event="oninput"`
  so the button unlocks live as you type. The confirm field resets each time the modal opens
  (`OpenDelete`) and after a successful delete, and `DeleteAsync` re-checks `DeleteConfirmed` as a
  defensive guard (the disabled attribute is UI-only).
- **Scope: entire-collection deletes only.** Bulk delete of selected events/contacts (ADR 0055) keeps
  its plain confirm — those are Trash-recoverable, so a name-typing gate there would be friction without
  a matching risk.
- **Client-only.** No API/DTO/schema change; the existing owner-only `DELETE /api/{calendars|address-books}/{id}`
  is unchanged. Styling reuses the global input rules (`.confirm-hint`/`.confirm-input` in `app.css`).

The Delete trigger lives in the ribbon `SectionContent`, so the bUnit guard
(`CollectionDeleteGuardTests`, ADR 0063) hosts each page under a matching `SectionOutlet` to render the
ribbon, opens the modal, and asserts the confirm button is disabled until the exact name is typed
(a wrong-case near-miss stays disabled; the exact name with incidental whitespace unlocks it).

## Consequences

- A populated calendar/address book can no longer be deleted by a double-click reflex — the user must
  deliberately reproduce its name.
- The message "can be undone by an administrator" is now the *only* recovery story, which is
  unsatisfying; the planned self-service restore (owner-visible deleted-collections list + one-click
  restore over the existing `IsDeleted` flag) is the proper fix and gets its own ADR.
- Introduces the first **type-to-confirm** pattern in the client; reuse it for any future irreversible,
  non-self-service-recoverable action rather than inventing a new confirmation each time.

## Deferred

- Self-service **restore of a deleted collection** (and an owner-visible list of deleted collections) —
  next unit, its own ADR.
- Extending type-to-confirm to other destructive actions (e.g. account-level takeout wipe) if any arise.
