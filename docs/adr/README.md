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
| [0030](0030-phase2-attendees-and-free-busy.md) | Phase 2 attendees & free/busy (CalDAV free-busy path) |
| [0031](0031-phase2-itip-scheduling.md) | Phase 2 iTIP scheduling (RFC 6638 REQUEST/REPLY/CANCEL) |
| [0032](0032-local-tls-proxy.md) | Local HTTPS via a Caddy TLS reverse proxy |
| [0033](0033-enterprise-logging.md) | Enterprise logging (Serilog, structured, six-level severity grading) |
| [0034](0034-web-client-shell-redesign.md) | Web client shell redesign (bottom tabs + ribbon + account box) |
| [0035](0035-user-profile-photo.md) | User profile photo (client-normalized 256×256 PNG, server byte-guard) |
| [0036](0036-contacts-master-detail.md) | Contacts master-detail + raw vCard editing (ribbon modals) |
| [0037](0037-contact-photo-cache.md) | Server-side contact-photo caching (lazy fetch, SSRF-guarded, embed-on-death) |
| [0038](0038-calendar-list-and-grid-views.md) | Calendar list + month/week grid views (extracted LOCATION field) |
| [0039](0039-zip-archive-import.md) | Zip-archive import (multi-file .ics/.vcf, e.g. a Google export) |
| [0040](0040-zip-import-into-separate-collections.md) | Zip import into separate new collections (recreate a Google export's structure) |
| [0041](0041-collection-management.md) | Collection management: rename, delete, import merge-by-name |
| [0042](0042-move-entries-between-collections.md) | Move single entries between collections |
| [0043](0043-dav-query-filter-evaluation.md) | DAV query-filter evaluation (calendar-query / addressbook-query) |
| [0044](0044-mutation-testing-stryker.md) | Mutation testing with Stryker.NET (Infrastructure, informational) |
| [0045](0045-rest-ui-invitations.md) | REST + web-UI invitations (accept/tentative/decline) |
| [0046](0046-sharing-ui-and-shared-with-me.md) | Sharing UI modernization + "shared with me" |
| [0047](0047-email-imip-per-tenant-smtp.md) | Email iMIP via per-tenant SMTP (external attendees) |
| [0048](0048-attendee-delete-as-decline.md) | Attendee delete as decline (REPLY;PARTSTAT=DECLINED) |
| [0049](0049-live-updates-signalr.md) | Live updates over SignalR (collections + invitation badge) |
| [0050](0050-recurring-event-editing.md) | Recurring-event editing (structured RRULE editor + grid expansion) |
| [0051](0051-per-instance-recurrence-edits.md) | Per-instance recurrence edits + monthly Nth-weekday |
| [0052](0052-webdav-push.md) | WebDAV-Push (Web Push notifications to native DAV clients) |
| [0053](0053-itip-on-per-instance-edits.md) | iTIP scheduling on per-instance recurrence edits |

The top-level specification tying these together: [`docs/spec.md`](../spec.md).
