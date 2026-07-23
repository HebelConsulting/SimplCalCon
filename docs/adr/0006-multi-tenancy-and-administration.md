# ADR 0006 — Multi-tenancy with platform and tenant administrators

## Status
Accepted (2026-07-23, spec interview)

## Context
SimplCalCon targets multiple isolated organizations on one deployment (hosted
offering for small orgs, ADR 0014), each self-managing its members, while the
operator manages tenants.

## Decision
- **Tenant** is the hard isolation boundary: every user, group, collection, object,
  ACL entry, and app password belongs to exactly one tenant; every query is
  tenant-scoped at the application layer.
- **Platform administrators** (tenant-less principals) manage tenant lifecycle
  (create/suspend/delete) and platform diagnostics; they do **not** read tenant data
  through normal APIs.
- **Tenant administrators** manage their tenant's users (invite/registration flow,
  disable, delete-with-takeout), groups, and tenant defaults (trash retention,
  version-history pruning policy, quotas — policies land in Phase 3).
- **Groups** are tenant-scoped principal sets usable in ACLs (ADR 0007).
- Accounts are **local** in v1. External IdP federation (Entra, Keycloak, …) is
  deferred to Phase 3 and will get its own ADR.
- Cross-tenant interaction is **none** in v1 — scheduling (ADR 0008) is
  within-tenant only until iMIP provides the external-attendee path.

## Consequences
- Tenant scoping must be structural (query filters/repository layer), not
  per-endpoint discipline; a test guard asserts no unscoped access path exists.
- On-device DAV URLs embed nothing tenant-identifying beyond the user's principal
  path; usernames are unique per tenant, and the login identifier is the user's
  e-mail address (globally unique across the deployment) to keep device setup
  simple.
