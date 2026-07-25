# ADR 0058 — "Shared by me" owner aggregate

## Status

Accepted — implemented. Completes an ADR 0046 deferred item.

## Context

ADR 0046 gave sharees a "shared with me" view, but an **owner** had no single place to see
everything *they* had shared — they had to open each calendar/address book's Share editor one by
one. ADR 0046 deferred the owner "shared by me" aggregate.

## Decision

Add **`GET/HEAD /api/shared-by-me`** (`SharedByMeController`): the caller's **owned** calendars +
address books that have ≥1 ACL grant, each with its resolved grants. Reuses the existing pieces —
`IDavRepository.ListAccessible*` filtered to `OwnerId == caller`, `IAclService.ListGrantsAsync`,
and `IPrincipalDirectory` for grantee names — returning `SharedByMeResource { Id, Kind, Name,
Shares: ShareResource[] }` (each share = principal + kind + rights). Collections with no grants are
omitted. No schema change (ACL + collections already exist).

The web `/shared` page (already the "shared with me" view) gains a **"Shared by me"** section:
each shared collection lists who it's shared with (name, `(group)` tag, rights) with a **Manage**
link to the existing `ShareEditor` (`/share/{kind}/{id}`), and the collection name opens it.

## Consequences

- Owners get an at-a-glance view of their sharing and a direct path to manage each grant, alongside
  what's shared with them — the sharing story is symmetric.

## Deferred

- **Group membership in the `ShareEditor`** (the other ADR 0046 deferred item) — you can grant to a
  group, but editing a group's members is still out of scope.
- Per-object ACLs; a combined tenant-wide sharing report for admins.
