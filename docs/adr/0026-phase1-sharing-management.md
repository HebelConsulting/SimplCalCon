# ADR 0026 — Phase 1 sharing management (as built)

## Status
Accepted (2026-07-23, Phase 1 implementation). Completes [ADR 0023](0023-phase1-acl-sharing.md).

## Context
ADR 0023 built ACL enforcement, but grants could only be created via the service API,
and the DAV `current-user-privilege-set` still reported owner-all. This unit makes
sharing user-manageable and reconciles the DAV privilege reporting.

## Decision

**REST grant management (ADR 0009).** Typed sub-resources
`/api/calendars/{id}/shares` and `/api/address-books/{id}/shares`:
- `GET` — list grants (`ShareResource`: principal id/kind/display/email + kebab-case
  rights);
- `PUT {principalId}` — create/replace a grant from a rights array;
- `DELETE {principalId}` — revoke.

The caller must **own the collection or hold the `share`/`admin` right**
(`RequireCanShareAsync`), so a delegate can manage sharing (ADR 0007). Rights convert
between the flags enum and the API's kebab-case strings (`read`, `write-content`,
`create`, `delete`, `share`, `admin`) via `AclRights`. A cross-tenant grant surfaces as
**400 `CROSS_TENANT_SHARE`** (the `CrossTenantGrantException` is mapped in the Problem
Details handler).

**Grantee picker.** `GET /api/principals?q=` returns users and groups in the **caller's
tenant** (name/email match, capped) — `IPrincipalDirectory`. Tenant-scoped, no
cross-tenant leakage.

**DAV privilege reporting fixed.** `current-user-privilege-set` now reflects the
**caller's effective rights** (`DavPrivileges.From(AclRight)`), not owner-all: `read` →
`read`; `write-content` → `write`/`write-content`/`bind`/`unbind`; `admin` →
`write-properties`. The home-set and collection PROPFIND compute per-collection rights
(`DavControllerBase.EffectiveRightsAsync`, owner ⇒ all).

**Web UI.** A Share page (`/share/{kind}/{id}`) lists grants (with remove), searches
principals, and grants read / +edit / +re-share; reachable from the calendar and
contacts views.

## Consequences
- **Verified**: 58 tests pass. Sharing tests cover owner grant→list→revoke, a grant
  enabling REST access (403 → 200), managing shares without the right (403), the
  principals search, and the **DAV privilege-set showing `read` but not `write` for a
  read-only share**.
- **Deferred**: per-object ACLs (collection is the granularity); finer `admin`-vs-`share`
  management semantics; a group-membership management UI; and a sharing screen richer
  than the current list + picker.
