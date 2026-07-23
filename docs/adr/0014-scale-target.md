# ADR 0014 — Scale target: medium (hundreds of tenants)

## Status
Accepted (2026-07-23, spec interview)

## Context
The design scale drives indexing strategy, recurrence expansion, and database
choices. Options ranged from small self-hosted (≤100 users) to SaaS scale.

## Decision
Design for a **hosted offering for small organizations**:

- **Hundreds of tenants, thousands of users** per deployment.
- Calendars with **tens of thousands of events**; address books with thousands of
  contacts.
- Device sync load dominated by polling clients (iOS defaults) — CTag checks must
  be O(1) (stored value, never computed by scanning) and sync-collection queries
  index-only (ADR 0004, 0012).
- **PostgreSQL is the primary production target** at this scale; SQLite remains
  supported (ADR 0001) and is expected to serve small installs comfortably —
  documentation states the recommendation boundary (roughly: multi-hundred-user or
  write-heavy deployments should run PostgreSQL).
- **Single-node** deployment model: one Api container (vertically scaled) + one
  database. Horizontal scale-out of the Api is not designed for in v1 — but no
  in-memory state may be correctness-critical (change feed, sync sequences, jobs
  are DB-backed), so scale-out later is an optimization, not a redesign.

## Consequences
- Performance tests target the stated shapes (e.g. sync-collection on a 50k-object
  collection, free-busy across a 100-user tenant) rather than hypothetical SaaS
  extremes.
- Quotas/rate limits per tenant are Phase 3 policy items; the model reserves room
  (per-tenant counters) without enforcing in v1.
