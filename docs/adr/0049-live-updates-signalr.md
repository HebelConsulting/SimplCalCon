# ADR 0049 — Live updates over SignalR (web client)

## Status

Accepted — implemented.

## Context

The Blazor WASM client (ADR 0025) refreshed only on user action or navigation: an event
written from another device (CalDAV sync, a shared editor, an accepted invitation) did not appear
until the user reloaded, and the invitation badge (ADR 0045) refreshed only on navigation. The
seed plan (ADR 0012) always intended **SignalR** live updates for the web surface alongside the
sync-token/CTag backbone and DAV WebDAV-Push. Every write already bumps a collection's
`ChangeSequence` — the change signal exists; it just wasn't pushed anywhere.

## Decision

Push change signals to the connected web client over **SignalR**, so the calendar/contacts view
and the invitation badge update **live**.

- **Transport.** A `NotificationHub` mapped at **`/hub/notifications`**, authenticated with the
  same OpenIddict bearer scheme as `/api`. WebSockets can't set an `Authorization` header, so the
  client passes the access token in the **`access_token` query string**; a small middleware
  (before `UseAuthentication`) lifts it into the `Authorization` header for `/hub` paths, so
  OpenIddict validation authenticates it like any other request.
- **Group model.** On connect a connection auto-joins its **per-user group** (`user:{id}`) for
  invitation pushes. For collections it joins **per-collection groups** (`collection:{id}`)
  explicitly via a `Subscribe(collectionId)` hub method — the hub verifies the caller's `read`
  right first (ACL, ADR 0007) — so a change broadcasts only to clients that can see it, and a
  collection opened *after* connect (or a shared one) is covered. Membership is per-connection and
  auto-cleaned on disconnect; the client re-subscribes on reconnect.
- **Server signal.** An **`IChangeNotifier`** port (Application). The single write path
  `ObjectStore` fires **`CollectionChangedAsync`** after a Put/Restore/Delete commits; the
  schedule-inbox fires **`InvitationsChangedAsync(owner)`** after an iTIP message is delivered or
  drained. Fired **post-commit** (a client that reloads sees committed state) and **wrapped** so a
  push failure never fails a write. The Api provides the SignalR-backed implementation
  (`SignalRChangeNotifier`, `IHubContext`); **Infrastructure stays SignalR-free** — its default
  `NoOpChangeNotifier` is used by hosts that don't wire a transport (design-time, unit tests).
- **Client.** A `LiveUpdates` service manages the `HubConnection` (`WithAutomaticReconnect`,
  token via `IAccessTokenProvider`). It **debounces** `CollectionChanged` per collection (~300 ms),
  so a bulk import that writes N objects triggers **one** reload, not N. `CalendarView` /
  `Contacts` `Subscribe` to the collection they show and reload it on its change (preserving the
  contact selection if it survives); `MainLayout` refreshes the invitation badge on
  `InvitationsChanged`. Refresh-on-navigation stays as the fallback when the connection can't be
  established.

## Consequences

- Events/contacts and the invitation badge update without a manual reload, across devices and the
  DAV path (both go through `ObjectStore`).
- Purge (hard delete) does **not** notify — it doesn't bump `ChangeSequence` and clients already
  saw the tombstone (consistent with ADR 0028).
- Live updates are **best-effort**: any push/transport failure degrades to the pre-existing
  refresh-on-navigation behaviour; the write path is never affected.
- New dependency `Microsoft.AspNetCore.SignalR.Client` (Client + integration tests) — MIT, within
  the license allowlist.

## Scope / deferred

- **v1 surfaces:** calendar events, contacts, the invitation badge.
- **Deferred:** live refresh of the *shared-with-me* list and admin views; presence/typing;
  **WebDAV-Push** for native CalDAV/CardDAV clients (this ADR is the *web* client — native clients
  still poll via sync-token/CTag). The integration test drives the hub over LongPolling; the
  WebSocket query-token path is exercised by real browsers.
