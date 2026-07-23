# ADR 0012 — Change notification: sync-token + CTag baseline, WebDAV-Push and SignalR in v1

## Status
Accepted (2026-07-23, spec interview)

## Context
DAV clients poll by default (CTag compare, then sync-collection). iOS/macOS only
poll. DAVx⁵ and Tasks.org support the **WebDAV-Push** draft (developed by the DAVx⁵
team) for near-instant sync on Android. The web UI needs its own live-update channel.

## Decision
Three mechanisms, all shipped in v1 (Push in the sense of the phased spec: the
polling baseline is Phase 1, push channels land with the full product in Phase 2):

1. **Polling baseline (Phase 1, mandatory DAV plumbing)**: `getctag` per collection
   and **sync-collection REPORT with sync-tokens (RFC 6578)**. Sync-tokens are
   backed by a monotonically increasing per-collection change sequence; deletions
   (incl. trash, ADR 0011) are reported as removed resources. Tokens survive server
   restarts; an expired/unknown token yields the RFC-defined `valid-sync-token`
   error so clients fall back to full resync.
2. **WebDAV-Push (Phase 2)**: implement the draft spec — service detection
   properties, subscription registration (Web Push transport), and change
   notifications on collection updates. Degrades to polling for non-supporting
   clients (iOS/macOS unaffected either way).
3. **SignalR channel (Phase 2)**: the Blazor web UI subscribes to collection-change
   events for live view updates.

All three are fed by one internal **change feed** (the same per-collection sequence
that backs sync-tokens), so a write is recorded once and fanned out.

## Consequences
- The change sequence/feed is a Phase 1 core entity — push later attaches to it
  without reworking writes.
- WebDAV-Push tracks a *draft* spec: pin the implemented draft version in the
  implementation ADR and expect revisions.
- Web Push (VAPID keys, push-service delivery) adds an operational secret and
  outbound HTTP dependency — configuration-optional; the feature degrades to
  polling when unconfigured.
