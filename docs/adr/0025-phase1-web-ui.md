# ADR 0025 — Phase 1 web UI (REST resources + Blazor WASM)

## Status
Accepted (2026-07-23, Phase 1 implementation)

## Context
The first human client (ADR 0010). It needs a REST resource surface (only `/api/me`
and `/api/app-passwords` existed) and a Blazor WASM UI, served by the Api host.

## Decision

**REST resources (`/api`, ADR 0009).** Typed collection resources
`/api/calendars` + `/api/address-books` with nested objects
`.../{id}/events` + `.../{id}/contacts`. Each: list (accessible = own + shared), get,
create, update, delete — hypermedia envelope, ETag/`If-Match`, and **ACL enforcement**
(`read` for reads, `write-content` for writes; collection create/delete owner-only)
via `IAclService` in `ApiControllerBase`. **Structured JSON payloads**: reads return
extracted fields; writes take structured fields and `IObjectComposer` builds the
iCalendar/vCard blob (preserving the UID on update) and stores through `IObjectStore`.
New `INSUFFICIENT_RIGHTS` (403) and `NOT_FOUND` (404) `ApiException` areas.

**Blazor WASM client (ADR 0010).** OIDC via
`Microsoft.AspNetCore.Components.WebAssembly.Authentication` against the seeded public
SPA client (code + PKCE), with a `BaseAddressAuthorizationMessageHandler` attaching
access tokens to `/api` calls through a typed `ApiClient`. Pages: Home (collections +
create), agenda calendar view (list grouped by day + add event), contacts, app-password
management (create shows the secret once), with an auth-aware router / login display.
Plain Razor + CSS (no component library).

**Hosting.** The Api references the Client and
`Microsoft.AspNetCore.Components.WebAssembly.Server`; `UseBlazorFrameworkFiles()` +
`UseStaticFiles()` + `MapFallbackToFile("index.html")` — `GET /` → SPA, `/api` → REST,
`/dav` → DAV, one deployable. The Dockerfile publishes the WASM into the host (no
`wasm-tools` workload needed). The SPA's OIDC redirect origin must match where it is
served, so **`SimplCalCon:SpaClient:BaseUrl` must equal the serving origin** (the
seeded client's registered redirect URI); the demo compose sets it to
`http://localhost:9080`.

## Consequences
- **Verified**: full build green; 53 tests pass (25 unit + 28 integration) including the
  calendar+event and contact REST lifecycles, a foreign-collection 403, and the SPA
  index served at `/`. The Docker image builds with the client and a container serves
  the SPA shell + framework files + `/api`. The in-browser OIDC login/token flow is
  verified manually (a WASM browser flow isn't automated here).
- **Deferred**: the month/week **calendar grid**, rich inline event/contact editors, a
  **sharing-management** screen (grants are service-only so far, ADR 0023), and live
  updates (SignalR, ADR 0012).
- Unknown-property preservation applies to DAV round-trips; REST-authored objects are
  (re)generated from structured fields (ADR 0004).
