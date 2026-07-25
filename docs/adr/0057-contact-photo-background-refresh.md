# ADR 0057 — Background contact-photo refresh

## Status

Accepted — implemented. Completes an ADR 0037 deferred item.

## Context

Contact cards with an **external** PHOTO URL (Google exports) are cached server-side (ADR 0037), but
only **lazily** — a photo is (re)fetched on the first request, or when its 7-day cache expires, on
the *next* request. So a photo whose contact is never viewed is never refreshed, and a source URL
that dies is only self-healed (cached bytes embedded back into the card) when someone happens to
open the contact. ADR 0037 deferred a background refresh.

## Decision

A **`BackgroundService`** (`ContactPhotoRefreshService`) periodically refreshes stale external photo
caches, reusing the existing lazy path.

- **`IContactPhotoService.RefreshStaleAsync(batchSize)`** picks the oldest cache rows past the 7-day
  revalidate window and, for each: re-reads the card's PHOTO; if it's still an external URL, runs the
  same `ResolveUrlAsync` the on-request path uses — **re-fetches** a live URL (refreshing the cache)
  or **self-heals** a dead one (embeds the cached bytes into the card, deletes the cache row). A
  cache whose contact is gone or no longer references a URL is deleted as orphaned. Fetches stay
  SSRF- + byte-guarded (the same `ContactPhotos` HTTP client).
- **Schedule.** On by default, daily, in batches — config `SimplCalCon:ContactPhotos`
  (`RefreshEnabled` = true, `RefreshHours` = 24, `BatchSize` = 100). A failed cycle is logged and
  retried next interval.

No schema change (the cache table + fetch/embed logic already exist), no new dependency.

## Consequences

- External contact photos stay fresh and dead URLs become self-contained **without** waiting for a
  view — cards survive source rot on their own.
- Adds periodic outbound image fetches; bounded by `BatchSize` per cycle and the 7-day staleness gate
  (only stale rows are touched, so cycles are cheap and idempotent). Disable with `RefreshEnabled=false`.

## Deferred

- Proactively caching contacts with an external PHOTO that has **never** been cached (that still
  happens lazily on first view); conditional revalidation (ETag/Last-Modified); DAV-side caching.
