# ADR 0065 — Admin-list live refresh (group list)

## Status

Accepted — implemented. Completes the ADR 0049 deferred "admin live refresh"; follows ADR 0064's pattern.

## Context

The Admin tab's lists didn't update live for a second admin. In practice the tenants and users lists
are **read-only** (there are no create/update/delete endpoints for them — tenants come from bootstrap,
users from the activation/invitation flow), so the only **mutable** admin list is **Groups**
(create/delete a group, add/remove a member — all tenant-admin, tenant-scoped, ADR 0059). This makes
"admin-list live refresh" concretely a **group-list refresh** for a tenant's admins.

## Decision

A tenant-scoped **`AdminChanged`** signal on the existing `IChangeNotifier` port, pushed to a
per-tenant admin group.

- **Hub group:** `admin:tenant:{tenantId}`. `NotificationHub.OnConnectedAsync` auto-joins it when the
  connecting principal is a **tenant admin** (`role == "admin"`) with a `tenant_id` claim (alongside
  the existing per-user group).
- **Port:** `IChangeNotifier.AdminChangedAsync(Guid tenantId)`. `SignalRChangeNotifier` sends
  `AdminChanged` to `admin:tenant:{tenantId}`; no-op in `NoOp` + `WebPush`; `CompositeChangeNotifier`
  fans out.
- **Fired** post-commit (best-effort — never fails the operation) from `GroupService` on **create**,
  **delete**, **add-member**, and **remove-member** (the last two also still fire `SharesChanged` for
  the affected member, ADR 0064 — different audience: the member vs the tenant's admins).
- **Client:** `LiveUpdates` raises an `AdminChanged` event; `Admin.razor` (tenant-admin branch)
  subscribes and reloads the group list, unsubscribing on dispose.

## Consequences

- When one tenant admin adds a group or edits membership, another admin viewing the Admin tab sees it
  without reloading.
- Platform admins aren't given a live signal — the tenants list they see never changes in-app.

## Deferred

- Live refresh of tenants/users would only matter once those become mutable in-app. Presence remains
  out of scope (ADR 0049).
