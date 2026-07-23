# Architecture Decision Records — Index

Seed ADRs from the specification interview (2026-07-23). New decisions get the next
sequential number; a changed decision gets a *new* ADR superseding the old one
rather than an edit.

| ADR | Title |
|---|---|
| [0001](0001-technology-stack.md) | Technology stack: .NET, Clean Architecture, EF Core on PostgreSQL *and* SQLite |
| [0002](0002-dual-protocol-surface.md) | Dual protocol surface: CalDAV/CardDAV for devices, REST for our own clients |
| [0003](0003-in-house-dav-implementation.md) | Implement the WebDAV/CalDAV/CardDAV protocol layer in-house |
| [0004](0004-hybrid-storage-model.md) | Hybrid storage: original blob as source of truth + indexed fields |
| [0005](0005-authentication.md) | Authentication: OIDC for web/REST, per-device app passwords for DAV |
| [0006](0006-multi-tenancy-and-administration.md) | Multi-tenancy with platform and tenant administrators |
| [0007](0007-acl-sharing-model.md) | Full ACL model for sharing |
| [0008](0008-calendar-scope-and-scheduling.md) | Calendar scope: events + tasks; scheduling internal-first, iMIP later |
| [0009](0009-rest-api-conventions.md) | REST API conventions: house suite, JSON-only (no XML negotiation) |
| [0010](0010-web-ui-blazor-wasm.md) | Web UI: Blazor WebAssembly, first-class deliverable |
| [0011](0011-soft-delete-and-version-history.md) | Data safety: trash (soft-delete) + per-object version history |
| [0012](0012-change-notification-and-push.md) | Change notification: sync-token + CTag baseline, WebDAV-Push and SignalR |
| [0013](0013-data-portability.md) | Data portability: import/export, takeout, server-to-server migration |
| [0014](0014-scale-target.md) | Scale target: medium (hundreds of tenants) |
| [0015](0015-inherited-engineering-conventions.md) | Inherited engineering conventions from the sibling project |
| [0016](0016-phase1-identity-tenancy-data-model.md) | Phase 1 identity & tenancy data model |
| [0017](0017-ef-core-persistence-and-dual-provider-migrations.md) | EF Core persistence & dual-provider migration layout |
| [0018](0018-phase1-authentication-services.md) | Phase 1 authentication services |
| [0019](0019-rest-plumbing-and-versioning-deferral.md) | REST plumbing as built; media-type versioning deferred |
| [0020](0020-phase1-calendar-contact-object-store.md) | Phase 1 calendar/contact object store |
| [0021](0021-phase1-carddav-surface.md) | Phase 1 CardDAV surface |
| [0022](0022-phase1-caldav-surface.md) | Phase 1 CalDAV surface |
| [0023](0023-phase1-acl-sharing.md) | Phase 1 ACL sharing (as built) |
| [0024](0024-deployment-and-ci-foundation.md) | Deployment & CI foundation |
| [0025](0025-phase1-web-ui.md) | Phase 1 web UI (REST resources + Blazor WASM) |
| [0026](0026-phase1-sharing-management.md) | Phase 1 sharing management (REST grants + DAV privileges + UI) |
| [0027](0027-event-splitting.md) | Event splitting (split one event into two same-kind events) |
| [0028](0028-phase1-trash-and-version-history.md) | Phase 1 trash & version history (restore/purge + revisions) |
| [0029](0029-phase1-data-portability.md) | Phase 1 data portability (import/export + migration takeout) |

The top-level specification tying these together: [`docs/spec.md`](../spec.md).
