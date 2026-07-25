# ADR 0066 — Per-user collection colour override

## Status

Accepted — implemented. Layers on ADR 0062 (owner-set collection colour).

## Context

ADR 0062 gave each collection one **owner-set** colour, shown to everyone. Users wanted their **own**
colour for a collection (including collections shared with them) without changing what others see.

## Decision

A per-user colour that **overrides** the owner colour for that user's view only. Effective colour for
a user = **personal override ?? owner colour ?? palette fallback**.

### Schema

New table **`UserCollectionColors`** — `Id`, `UserId` (FK → users, cascade), `CollectionId` (FK →
`Collections`, cascade), `Color` (`varchar(32)`), unique `(UserId, CollectionId)`. Migrations both
providers. The ADR 0062 `Collection.Color` (owner default) is unchanged.

### REST

- Collection resources gain **`myColor`** (the caller's override, nullable) alongside the owner
  `color`. `CalendarsController`/`AddressBooksController` `List`/`Get` fetch the caller's overrides
  (`IUserCollectionColorService.GetOverrides/GetOverride`) and map them.
- **`PUT /api/{calendars|address-books}/{id}/color {color}`** and **`DELETE …/{id}/color`** set/clear
  the caller's personal colour — gated on **read** access (so sharees can recolour), no If-Match
  (it's the caller's own preference, upserted by `(user, collection)`). The owner-only
  `PUT …/{id} {name, color}` still sets the shared default.

### UI

- The pane's colour picker now sets the **caller's personal** colour for **everyone** (the ADR 0062
  owner-only lock is lifted); a **reset** (↺, shown only when a personal colour exists) clears it back
  to the default. `CollectionsPane.Item.Color` is the *effective* colour; `HasOwnColor` drives the
  reset. `OnColorChanged` → `SetMyColorAsync`, `OnColorReset` → `ClearMyColorAsync`.
- The owner sets the **shared default** in the **Edit** modal (the former Rename modal, now name +
  colour, owner-only). `CollectionColors.Effective(id, myColor, ownerColor)` computes the colour used
  for the pane swatch, list colour column, and grid chips everywhere.

## Consequences

- Every user can colour their calendars/address books to taste, including shared ones, without
  affecting anyone else; the owner colour becomes a suggested default.
- Two colour write paths now exist: owner default (collection PUT, If-Match) and personal (the
  `/color` sub-resource, read-gated).

## Deferred

- Syncing either colour to/from CalDAV `calendar-color`; a tenant-wide colour policy.
