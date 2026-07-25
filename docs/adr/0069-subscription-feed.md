# ADR 0069 — Read-only subscription feeds (ICS / VCF)

## Status

Accepted — implemented. Closes item 2 of the [Outlook gap analysis](../outlook-gap-analysis.md); also
a generic "subscribe" for Apple/Google/Thunderbird.

## Context

Clients that can't do full CalDAV/CardDAV (notably **Microsoft Outlook**, all variants) can still
**subscribe** to a read-only `.ics` calendar URL ("Internet Calendars"). The existing
`GET …/{id}/export` endpoint is auth-gated (OIDC/DAV Basic), which a subscription poller can't supply.
We need an anonymous, revocable feed URL.

## Decision

A per-collection **capability-token feed**: the token in the URL is the only credential.

- **Schema:** `Collection.FeedToken` (nullable `varchar(64)`, unique index; NULLs distinct on both
  providers). On the base `Collection`, so calendars **and** address books get it. `null` = disabled.
- **Content (anonymous, `FeedController`):**
  `GET/HEAD /api/calendars/{id}/feed/{token}.ics` → `text/calendar`, and
  `.../address-books/{id}/feed/{token}.vcf` → `text/vcard`, reusing `IObjectImportExport.ExportAsync`.
  The token is compared **constant-time**; a wrong/absent token → **404** (no existence leak). The
  feed deliberately **bypasses ACL** — it's a capability link the owner chooses to share.
- **Management (owner-only):** `PUT /api/{calendars|address-books}/{id}/feed` enables/**resets**
  (mints a fresh token, so a leaked link is rotated by calling it again); `DELETE …/feed` disables
  (clears the token → existing links 404). `IDavRepository.SetFeedTokenAsync` generates via
  `SecretGenerator`. The collection resource exposes `feedToken` **to the owner only**.
- **UI:** a "Subscription" section in the calendar/address-book **Edit** modal (owner-only) — enable,
  a copyable `https://…/feed/{token}.ics` URL (webcal:// works too), reset, and disable.

## Consequences

- Outlook (and any client) can subscribe to a **read-only** calendar without an add-in or an account;
  it doubles as a general subscribe/share-link feature.
- The token is a bearer-in-URL secret: anyone with the link reads the collection. It's per-collection,
  rotatable, and revocable, and never exposed to non-owners.
- `.vcf` feeds exist for symmetry, but mainstream clients don't subscribe to remote contact feeds the
  way they do calendars.

## Deferred

- Conditional GET / `ETag` on the feed (poll efficiency); a configurable feed refresh hint; scoping a
  feed to a time window.
