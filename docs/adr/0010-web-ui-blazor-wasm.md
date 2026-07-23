# ADR 0010 — Web UI: Blazor WebAssembly, first-class deliverable

## Status
Accepted (2026-07-23, spec interview)

## Context
A web interface for viewing/editing/backing up data is in scope from the start.
Options were Blazor WASM (C# end to end, sibling-project conventions carry over),
Blazor Server (server-held session state, poor fit for a long-open calendar app),
or a JS/TS SPA (second toolchain).

## Decision
**Blazor WebAssembly**, served by the Api host, consuming the `/api` REST surface
(ADR 0009) with OIDC (code + PKCE) auth, live-updated via the SignalR channel
(ADR 0012).

Feature scope (phased per spec §6): calendar views (month/week/day/agenda), task
lists, contact browsing/editing, collection management, sharing management
(ADR 0007), trash + version-history restore (ADR 0011), import/export/takeout
(ADR 0013), app-password management with device setup instructions (ADR 0005),
tenant-admin and platform-admin areas (ADR 0006).

## Consequences
- One language/toolchain across the repo; UI test patterns (bUnit-style guards) can
  follow the sibling project's approach.
- Calendar grid/recurrence rendering has no ready-made Blazor component of note —
  the calendar view is budgeted as a substantial in-house component (license
  constraint applies to any component library considered).
- Initial WASM payload size is accepted (an app users keep open); mitigations
  (AOT/trimming decisions) are implementation details, not spec items.
