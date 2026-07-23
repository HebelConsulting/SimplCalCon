# ADR 0023 — Phase 1 ACL sharing (as built)

## Status
Accepted (2026-07-23, Phase 1 implementation). Implements [ADR 0007](0007-acl-sharing-model.md).

## Context
ADR 0007 committed to a full ACL model. This unit builds the model, the effective-rights
evaluation, and enforcement across both DAV surfaces, so collections can actually be
shared and synced by devices — the access model shifts from owner-only to owner-or-granted.

## Decision

**Grant (`AclEntries`)**: `(Id, CollectionId, PrincipalId, Rights, CreatedAt,
ConcurrencyToken)`, unique per `(CollectionId, PrincipalId)`. `PrincipalId` is a user
or a group. `Rights` is the `[Flags] AclRight` enum (`Read`, `WriteContent`, `Create`,
`Delete`, `Share`, `Admin`) stored as int. The **owner holds all rights implicitly**
(no row).

**Effective rights (`IAclService`)**: for *(user, collection)* — owner ⇒ all; otherwise
the union of grants to the user and to every group they belong to **transitively**
(`PrincipalGraph` BFS over the membership graph, ADR 0016). **Cross-tenant grants are
rejected** (`CrossTenantGrantException`, ADR 0006). **Tenant admins get no implicit
access** — explicit grants only.

**Enforcement mapping**: `read` gates PROPFIND/REPORT/GET; `write-content` gates object
PUT and DELETE; **collection-level** operations (MKCOL/MKCALENDAR create, DELETE
collection) remain **owner-only**; `create`/`delete`/`share`/`admin` are stored and
reserved for finer control and the management surface. The DAV collection/object
controllers resolve the collection (owned by the route principal) and check the caller's
effective rights via `DavControllerBase.HasAccessAsync`; the object store stays trusted
(the controller gates).

**Discovery**: a shared collection stays at the owner's URL; the sharee's home-set
lists it there (`IDavRepository.ListAccessible{AddressBooks,Calendars}Async` = own +
read-granted), rendered with an href to `/dav/{addressbooks|calendars}/{ownerId}/{name}/`.

## Consequences
- **Verified**: unit tests (owner-all, stranger-none, direct grant, nested-group
  transitivity, cross-tenant rejection) + an integration test proving the end-to-end
  flow — denied before grant, read after a read grant (with the collection appearing in
  the sharee's home-set), write denied on a read-only grant, write allowed after a
  write-content grant, and a third user still denied.
- **Deferred**: the REST/web **ACL management surface** (grants are created via
  `IAclService` + tests for now); DAV `current-user-privilege-set` still reports
  owner-all rather than the caller's real grants (ADR 0021/0022) — to be reconciled with
  the management unit; **per-object** ACLs (the collection is the sharing granularity);
  a tenant-admin override (deliberately absent).
- The object store, revisions, tombstones, and sync all work unchanged for sharees —
  enforcement is purely at the DAV controller boundary.
