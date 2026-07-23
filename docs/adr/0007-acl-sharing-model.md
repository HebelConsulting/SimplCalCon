# ADR 0007 — Full ACL model for sharing

## Status
Accepted (2026-07-23, spec interview)

## Context
Calendars and address books must be shareable within a tenant (family/team
calendars, shared company address book) with more nuance than read/read-write —
e.g. delegating administration of a team calendar without transferring ownership.

## Decision
- Every **collection** carries an ACL: grants of rights to **principals** (users or
  groups) of the same tenant.
- Rights (per grant, combinable): **read**, **write-content** (modify existing
  objects), **create**, **delete**, **share** (manage grants below admin), **admin**
  (full control incl. collection properties and deletion).
- The **owner** implicitly holds all rights; ownership is transferable by a tenant
  admin.
- Effective rights = union over direct and group grants; there are no deny entries
  (absence of a grant is the only "no").
- Enforcement lives in the application layer and applies identically to both
  surfaces (ADR 0002). Scheduling operations (ADR 0008) act under the attendee's
  own rights on their own collections.
- **Surfaces**: managed via REST/web UI. The DAV surface reports privileges
  read-only (`current-user-privilege-set`, `DAV::acl` per RFC 3744 subset); the DAV
  `ACL` method is not supported in v1.
- Shared collections appear in the grantee's DAV home set automatically (that's how
  device clients see them).

## Consequences
- Fine-grained rights add spec/UI surface everywhere collections appear; the web
  UI's sharing dialog is the canonical management experience.
- No deny entries keeps evaluation trivially order-independent — revisit only with
  concrete evidence of need.
- Per-object ACLs are out of scope; the collection is the sharing granularity.
