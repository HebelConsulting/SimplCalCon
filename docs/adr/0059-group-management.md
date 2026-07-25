# ADR 0059 — Tenant-admin group + membership management

## Status

Accepted — implemented. Completes the last ADR 0046 deferred item ("group membership in the editor").

## Context

ACL grants can target a **group** (ADR 0007), and effective rights are evaluated over transitive
group membership (`PrincipalGraph`). But groups could only be **seeded** — there was no API or UI
to create a group or manage its members, so group-based sharing wasn't actually usable. ADR 0046
deferred "group membership in the editor".

## Decision

Manage groups on the **Admin tab, tenant-admin only** — *not* in the per-collection `ShareEditor`.

**Why not the editor:** group membership is **tenant-wide** — adding a user to a group grants them
access to *every* collection shared with that group. Letting a collection owner (a non-admin) edit
members from a per-collection Share dialog would be a privilege-escalation path. So management is a
tenant-admin function; the `ShareEditor` keeps *granting* to groups (already possible via the
principal search), and now those groups can be populated.

- **`IGroupService`** (Infrastructure `GroupService`, tenant-scoped): list groups (with member
  counts), create (rejects a duplicate name), delete (drops memberships in both directions), list
  members (resolved via `IPrincipalDirectory`), add member (same-tenant only; idempotent; a nesting
  cycle → `WouldCycle`), remove member. Cycles are caught from the DbContext invariant.
- **API** on `AdminController` (`RequireTenantAdminAsync`-gated): `GET/POST /api/admin/groups`,
  `DELETE …/{id}`, `GET/PUT/DELETE …/{id}/members[/{principalId}]`. Duplicate → 409, cycle → 409,
  missing → 404.
- **UI**: a **Groups** section on the Admin tab — create/delete groups and, per group, add members
  via the principal search (reused) + remove. Members can be users or nested groups (shown `(group)`).

No schema change (`Group` + `GroupMembership` already exist), no new dependency.

## Consequences

- Group-based sharing is now end-to-end usable: an admin creates a group + adds members, any owner
  shares a calendar/address book with it (ShareEditor), and every member gets access transitively.
- Group management is correctly scoped to tenant admins; owners can't silently widen a group's reach.

## Deferred

- Managing/viewing group membership from within the `ShareEditor` (even read-only) — owners see the
  group name in the grant but manage membership via an admin.
