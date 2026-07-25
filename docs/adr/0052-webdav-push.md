# ADR 0052 — WebDAV-Push (native-client change notifications)

## Status

Accepted — implemented (protocol complete; DAVx5 interop is manual acceptance).

## Context

The web client gets live updates over SignalR (ADR 0049), but native CalDAV/CardDAV clients still
poll (sync-token/CTag). The **WebDAV-Push** draft (bitfire, namespace
`https://bitfire.at/webdav-push`) standardises pushing change notifications to DAV clients over
**Web Push** (RFC 8030) — this is what **DAVx5** (Android) implements. (Apple's clients use their
own proprietary APNs push, *not* WebDAV-Push, so they don't benefit.) ADR 0049 already added the
exact server-side hook: `IChangeNotifier.CollectionChangedAsync` fires post-commit on every write.

## Decision

Implement WebDAV-Push as a second transport on the shared change signal.

- **Advertise (PROPFIND).** When enabled, a collection advertises `push:transports` →
  `web-push` → `vapid-public-key` (the server VAPID key, `type="p256ecdsa"`), a stable
  `push:topic` (base64url of the collection id), and `push:supported-triggers` (content-update).
  Added in `DavPushAdvertisement` + `DavNames`; the `MultiStatus` builder declares the `P:` xmlns.
- **Register / unregister.** `POST` a `push-register` document to the collection URL (read access
  + push enabled required, else `403 push-not-available`) → stores/upserts a subscription
  (endpoint + p256dh + auth) and returns **`204`** with an absolute `Location` (the registration
  URL) and an `Expires` header (server-capped TTL). `DELETE` that registration URL unsubscribes.
  `WebDavPushController` (routes on the calendar + address-book collection URLs +
  `/dav/push-subscriptions/{id}`).
- **Deliver.** `WebPushChangeNotifier : IChangeNotifier` fires on `CollectionChangedAsync`: it
  encrypts a `push-message` (the `topic` + a `{DAV:}sync-token`, matching `DavTokens.Format`) and
  sends it to every subscription via the **WebPush** library (RFC 8291 aes128gcm + RFC 8292 VAPID);
  the client then pulls with the existing `sync-collection` REPORT. A `404`/`410` (gone) or expired
  endpoint is **pruned**. It's composed with the SignalR notifier by `CompositeChangeNotifier`
  (each transport isolated — one failing never blocks the others or the write).
- **VAPID keys.** A `SimplCalCon:WebPush` config section (`VapidPublicKey`/`VapidPrivateKey`/
  `Subject`); **disabled when absent** (not fail-fast — WebDAV-Push is optional). In development an
  **ephemeral** pair is generated (`AllowEphemeralKeys`) so the demo works — logged with a warning
  that subscriptions reset on restart. Production should configure a persistent pair (like the OIDC
  certs); a rotated key invalidates subscriptions and clients re-register.

### Schema

One table **`PushSubscriptions`** (FK → `Collections`, cascade): `Endpoint` + `P256dh` + `Auth`
(base64url) + `ExpiresAt`, unique on `(CollectionId, Endpoint)` for idempotent re-registration.
No existing table touched. Migrations for both providers.

### Dependency

`WebPush` (MIT) — handles the RFC 8291/8292 crypto. It and its transitive `Portable.BouncyCastle`
declare their license by URL, so both are pinned to MIT in `build/licenses/package-overrides.json`
(version-pinned; a bump re-triggers the gate).

## Consequences

- DAVx5-class clients can receive pushed changes instead of polling; changes flow from any write
  (REST or DAV) through the same post-commit signal.
- Purge (hard delete) doesn't notify (no `ChangeSequence` bump), consistent with ADR 0028/0049.

## Verification / deferred

- Automated integration tests cover capability advertisement, registration (`204` + `Location` +
  `Expires`), the write→push fan-out (topic + sync-token, via a capturing sender), and unregister.
  The **RFC 8291 encryption + real DAVx5 interop is manual acceptance** (no device/push service in
  CI) — tracked in `docs/dav-client-matrix.md`.
- **Deferred:** property-update triggers; per-object (`Depth`) push scoping; push for the
  schedule-inbox; VAPID key-rotation push messages; `Urgency`/`Topic` header tuning.
