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

**Client (Blazor).** A reusable **`AvatarEditor`** component (parameterized by the photo
`Path`) renders the avatar and drives upload: `InputFile` →
`RequestImageFileAsync("image/png",1024,1024)` downscale → interactive crop → PUT the raw
bytes; plus a Remove. It is used for **self** on the **Profile** page (`/profile`, from the
account menu) and, for a **tenant admin**, for a **selected user** in the **Admin** tab
(`Path = api/users/{id}/photo`, `@key`-ed per user). **Render gotcha:** the endpoint is
bearer-protected, so a plain `<img src>` would 401 — the client **fetches the bytes with the
authenticated `ApiClient` and renders a `data:` URL** (shell avatar + editor). Fallback to
initials when there's no photo.

**Interactive crop.** `photoCrop.js`'s `create(canvas, dataUrl)` shows a fixed square/round
frame (the canvas) with the image **pan + zoomable** behind it (pointer drag + wheel + a
zoom slider); the visible frame *is* the crop, so `toPng(size)` re-draws the same framed
region at 256×256. The whole interaction runs in JS (no per-drag Blazor round-trips); Blazor
holds the returned handle (`setZoom`/`toPng`/`dispose`).

## Consequences
- Zero image-decoding surface on the server; one small table; deletes cascade cleanly.
- The client sends only a clean square 256×256 PNG regardless of the source aspect ratio.

## Deferred
Small photo thumbnails in the Admin user *list* (the detail pane shows the selected user's
avatar); a shared-state push so changing your **own** photo updates the shell avatar without
a reload; touch-gesture zoom on the crop frame.
