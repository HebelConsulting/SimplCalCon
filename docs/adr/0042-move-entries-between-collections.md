# 0042 — Move entries between collections

## Status

Accepted (2026-07-24).

## Context

Users need to move a single event to another calendar, or a contact to another address book — e.g.
after importing a Google export into separate calendars, to reorganise a few misfiled entries.

## Decision

A **move** verb sub-resource (a genuine state transition, ADR 0009):

- `POST /api/calendars/{id}/events/{eventId}/move` and
  `POST /api/address-books/{id}/contacts/{contactId}/move`, body `{ targetId }`.
- The controller reads the source object's blob and **writes it to the target via `IObjectStore`**
  (preserving the UID and blob — so the item, not a re-composed copy, moves), then **deletes it from
  the source**. `If-Match` on the source object; **`write-content` required on both** source and
  target; the target must be the **same kind** (a calendar for an event, an address book for a
  contact) and is reachable only within the caller's tenant (ACL, ADR 0007).
- A UID already present in the target → **`409 MOVE_CONFLICT`**.
- UI: a **"Move to…"** picker (the other collections of the same kind) in the event / contact detail
  view; moving reloads the list.

## Consequences

- Single-entry transfer between collections, from the web UI.
- **Non-atomic** (write-to-target then delete-from-source), like event split (ADR 0027): a failure
  between the two steps could leave the item in both — acceptable and self-correcting on a retry.
- **Simplifications:** same-kind only; the target write reuses the source resource name (which is
  UID-derived, so a same-UID target is the same logical object; a different-UID/same-resource-name
  clash across collections is theoretical); **no multi-select bulk move** yet.
