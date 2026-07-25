# ADR 0064 — Live refresh for sharing (SharesChanged over SignalR)

## Status

Accepted — implemented. Completes part of the ADR 0049 deferred "shared-with-me/admin live refresh".

## Context

Sharing changes weren't pushed live: after an owner granted or revoked access — or a tenant admin
changed a group's membership — the affected user's **Shared with me** page (and the owner's **Shared
by me**) only updated on navigation/reload. ADR 0049's SignalR hub already pushes per-collection
(`CollectionChanged`) and per-user (`InvitationsChanged`) signals; this extends it to sharing.

## Decision

A new **`SharesChanged`** signal on the existing `IChangeNotifier` port, pushed to the affected
users' per-user groups, so the `/shared` page reloads both sections live.

- **Port:** `IChangeNotifier.SharesChangedAsync(IReadOnlyCollection<Guid> userIds)`. Implemented by
  `SignalRChangeNotifier` (→ `Clients.Groups(user:{id}…).SendAsync("SharesChanged")`), and a no-op in
  `NoOpChangeNotifier` and `WebPushChangeNotifier` ("shared with me" is a web-client concept; native
  DAV clients see shared collections in their home-set). `CompositeChangeNotifier` fans out.
- **Who is notified** (computed post-commit, best-effort — a push failure never fails the change):
  - **Grant / revoke** (`AclService`): the collection **owner** (their "shared by me" changed) plus
    the grantee — resolved via the new `PrincipalGraph.GetMemberUserIdsAsync`, which returns the user
    itself or, for a group, every **transitive** user member (nested groups included).
  - **Group membership change** (`GroupService.AddMember`/`RemoveMember`): the affected member's
    transitive users (they gained/lost access to whatever is shared with that group).
- **Client:** `LiveUpdates` raises a `SharesChanged` event (from the hub message); `SharedWithMe.razor`
  (the `/shared` page) subscribes and reloads `shared-with-me` + `shared-by-me`, unsubscribing on
  dispose. The hub connection auto-joins the user group (ADR 0049), so no explicit subscribe is needed.

## Consequences

- Grant/revoke and group-membership edits reflect on the sharee's and owner's screens within a moment,
  no reload — matching the calendar/contacts live behaviour.
- Group grants correctly reach every (transitive) member, reusing the ACL graph in reverse.

## Deferred

- **Admin-list** live refresh (tenants/users/groups) — lower-frequency, admin-initiated; still
  reload-on-navigation. Presence remains out of scope (ADR 0049).
