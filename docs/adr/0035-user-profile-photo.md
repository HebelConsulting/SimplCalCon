# ADR 0035 — User profile photo

## Status
Accepted (2026-07-24). Extends the account UI ([ADR 0034](0034-web-client-shell-redesign.md)).

## Context
The shell avatar showed initials with a "photo later" placeholder. We want an uploadable
profile photo, following the sister project's proven design (SimplArchive ADR 0310): the
**server does zero image processing** — clients normalize to a fixed 256×256 PNG, the server
only guards the bytes. This removes an entire class of server-side image-decoding CVEs and
dependencies. Schema was approved before migrating (both providers).

## Decision

**Storage — a 1:1 shared-primary-key companion table.** `UserProfilePhoto`
(`Infrastructure`): `UserId` is **both PK and FK → Users**, `OnDelete: Cascade`; `TenantId`
FK → Tenants `Restrict` (nullable, tenant-scoped, indexed); `Photo` (bytea/BLOB, NOT NULL);
`UpdatedAt` (UTC). Shared-PK keeps the frequently-queried user row lean and cascades the
photo away with the user. Migrations `AddUserProfilePhoto` on SQLite **and** Postgres — one
new table, nothing else touched.

**Server byte-guard, no image library.** `ProfilePhotoValidator.IsValid` is pure byte
parsing: ≤ 1 MB, PNG signature, first chunk `IHDR`, width/height (big-endian from IHDR)
1…1024 — else `400 INVALID_PROFILE_PHOTO` (`Errors/Exceptions/Users`). Cheap because the
client always sends a clean 256×256 PNG.

**Endpoints (`UsersController`, `api/users`).** `PUT`/`GET`/`HEAD`/`DELETE
/api/users/{id}/photo` (+ `…/me/photo`). Auth = **self, or a tenant admin acting on a user
in their own tenant**. `GET` returns `image/png` (200) or 404; the raw PNG is read from
`Request.Body` (no model binding). `GET /api/me` gains a computed **`HasPhoto`** flag so the
client knows whether to fetch.

**Client (Blazor).** `InputFile` → `RequestImageFileAsync("image/png",1024,1024)` downscale
→ `photoCrop.js` (ES module) crops the largest centered square onto a 256×256 canvas →
`toDataURL('image/png')` → PUT the raw bytes. The **Profile** page (`/profile`, reached from
the account menu) shows the large avatar + Change / Remove. **Render gotcha:** the endpoint
is bearer-protected, so a plain `<img src>` would 401 — instead the client **fetches the
bytes with the authenticated `ApiClient` and renders a `data:` URL** (shell avatar +
profile). Fallback to initials when there's no photo.

## Consequences
- Zero image-decoding surface on the server; one small table; deletes cascade cleanly.
- The crop is a **centered square** (not an interactive pan/zoom box) — simpler JS, same
  256×256 result; a draggable/resizable crop can be added later without server changes.

## Deferred
Admin UI to change/remove **other** users' photos (the endpoints + tenant-admin authz
already exist — only the admin-side dialog is missing); interactive crop; avatars in the
Admin user list.
