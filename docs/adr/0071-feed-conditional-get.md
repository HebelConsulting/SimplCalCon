# ADR 0071 — Conditional GET (ETag) on the subscription feed

## Status

Accepted — implemented. Completes the ADR 0069 deferred "conditional GET / ETag on the feed".

## Context

Subscription feeds (ADR 0069) are polled by clients on their own schedule (often hourly). Each poll
re-ran the full `ExportAsync` and re-sent the whole `.ics`/`.vcf`, even when nothing had changed —
wasteful for the server and the network.

## Decision

Support conditional GET on the feed endpoints with the collection's **`ConcurrencyToken`** as the ETag.

- The token regenerates on **any** collection-row change: object writes bump the collection's
  `ChangeSequence` (a tracked update → new token), and rename/recolour update it directly. So it
  changes whenever the exported content could — a sound (slightly conservative) ETag.
- `FeedController` sets `ETag: "{ConcurrencyToken}"` and `Cache-Control: private, max-age=0,
  must-revalidate` on every feed response. On a request whose `If-None-Match` matches (weak
  comparison per RFC 7232 — `*` or the tag, ignoring a `W/` prefix), it returns **304 Not Modified
  before running the export** — so an unchanged poll costs a cheap collection lookup, not a full
  serialize + transfer.

No schema change; the ETag derives from data the feed already loads to validate the token.

## Consequences

- A polling subscriber that has the current version gets a small `304` instead of the full document,
  and the server skips the export on those polls.
- The ETag can change without the content changing (e.g. a recolour) — a harmless extra refresh, never
  a stale `304`.

## Deferred

- `Last-Modified` / `If-Modified-Since`; a tunable `Cache-Control` max-age hint.
