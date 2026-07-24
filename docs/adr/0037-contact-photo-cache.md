# 0037 — Server-side contact-photo caching

## Status

Accepted (2026-07-24).

## Context

Contact cards (vCard) may carry their photo one of two ways: **inline** (base64 in the card, or a
`data:` URI) or as an **external URL** (`PHOTO:https://…`) — Google Contacts exports the latter.
External URLs are fragile:

- Browsers loading them cross-origin get **referrer-blocked / throttled** (e.g. Google's photo host
  returned `429` to the SPA; ADR 0036 worked around it with `referrerpolicy="no-referrer"`).
- The URL can simply **die** — the photo host expires the link, the account is deleted — and the
  card is then permanently pictureless even though the image was once available.

We want a contact's photo to be **durable and reliably displayable** regardless of the source's
later behaviour, without forcing every card to be rewritten up front.

## Decision

A **lazy server photo endpoint backed by a small cache table**, with a **fall-back-then-embed**
step so a dying source is healed permanently.

- **`GET /api/address-books/{id}/contacts/{cid}/photo`** (+ `HEAD`) returns the contact's photo
  bytes; `404` when the card has none. `read` right required (ADR 0007). The SPA's detail pane
  fetches this endpoint (authenticated → `data:` URL) instead of pointing `<img>` at the raw URL,
  which also retires the `referrerpolicy` workaround.
- Resolution (`IContactPhotoService` / `ContactPhotoService`, Infrastructure):
  - **Inline / `data:` photo** → decode from the card and serve; nothing is cached (it already
    lives in the card).
  - **External URL** → serve the cached copy while fresh (same URL, within a **7-day** revalidate
    window); otherwise **fetch server-side** (no browser, so no referrer problem), byte-guard it,
    and cache it in `ContactPhotos`.
  - **Source failure with a cached copy present** → serve the cache **and embed** the cached bytes
    back into the card as an inline base64 `PHOTO` (via the one `IObjectStore` write path, so the
    change bumps the collection change-sequence and syncs to DAV clients). The card is now
    **self-contained**; the redundant cache row is dropped.
  - **Source failure with no cache** → `404`.
- **SSRF defence** (`SsrfSafeConnect`): we fetch attacker-controllable URLs from imported cards, so
  the "ContactPhotos" `HttpClient` validates the **resolved IP at connect time** (via
  `SocketsHttpHandler.ConnectCallback`) and refuses loopback / private / link-local / CGNAT /
  ULA / multicast addresses. Validating at connect time (not just parse time) also defeats DNS
  rebinding and redirects that land on an internal host. Plus: http/https only, **5 s** timeout,
  **≤3** redirects, **5 MB** cap, `image/*` content-type only.
- **Schema** — one new table `ContactPhotos`, a shared-PK 1:1 companion to the contact object
  (mirrors `UserProfilePhoto`, ADR 0035): `ObjectId` (PK **and** FK → `Objects`, cascade),
  `TenantId` (FK → `Tenants`, restrict), `Photo` (bytea/BLOB), `ContentType`, `SourceUrl`
  (invalidates the cache when the card's PHOTO URL changes), `FetchedAt` (UTC). Migrations for both
  providers (`AddContactPhotoCache`).

## Consequences

- A card whose photo host dies keeps its picture, and after the next view becomes permanently
  self-contained (the photo then also travels with export/DAV sync).
- Photos are fetched **only when actually viewed** — no import-time network I/O, no fetching photos
  nobody looks at.
- Embedding on failure means a `GET` can trigger a write (a deliberate repair action, like the
  If-Match-exempt recovery actions in ADR 0028). It is idempotent — once embedded, the card takes
  the inline path.
- Server-side fetching is an SSRF surface; the connect-time IP guard is the primary mitigation.
- **Deferred:** proactive/background refresh of stale caches; caching for the DAV surface (this is
  REST/UI only — DAV clients fetch external URLs themselves); an admin view of cache size; ETag /
  `Last-Modified` conditional revalidation against the source.
