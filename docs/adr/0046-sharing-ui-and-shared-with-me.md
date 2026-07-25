# 0046 — Sharing UI modernization + "shared with me"

## Status

Accepted (2026-07-25).

## Context

ADR 0026 made sharing user-manageable — REST grant endpoints, a `/share/{kind}/{id}` page, and DAV
`current-user-privilege-set` reflecting real grants. Two rough edges remained:

1. The Share UI is a **full-page route**, out of step with the app's in-context **ribbon Modal** UX
   (Contacts/Calendar), offers only coarse read / edit / re-share **checkboxes**, and can't **edit** an
   existing grant (only remove + re-add).
2. There is **no "shared with me" view** — shared collections show in the switchers with a
   "(shared)" tag, but not *who* shared them or *what rights* the user has.

## Decision

- **`ShareEditor`** — a reusable component with **role presets** (**Viewer** = read, **Editor** =
  read + write-content, **Manager** = read + write-content + share), **editable in place** (change a
  grantee's role → `PUT` replaces the grant, which the REST already supports), plus add-by-search and
  remove. It's opened from a **Share Modal** in the Calendar + Contacts ribbons (replacing the
  full-page link); the `/share/{kind}/{id}` route stays and now just renders `ShareEditor`.
- **Shared with me** — `GET`/`HEAD /api/shared-with-me` (`SharedWithMeController`) lists the caller's
  **non-owned** accessible calendars + address books with the **owner's display name** and the
  caller's **effective rights** (`IAclService.GetEffectiveRightsAsync` + `IPrincipalDirectory`). A
  `/shared` page renders them (linked from Overview) and opens each in its tab.

## Consequences

- Sharing is in-context and editable with clear roles; users can see what's shared with them and by
  whom. Reuses the existing REST grant path (`PUT` = create-or-replace).
- Presets surface read / write-content / share; the `Create`/`Delete`/`Admin` ACL flags aren't
  exposed in the UI (`write-content` already governs object writes, collection ops stay owner-only).
- **No schema change.** Deferred: per-object ACLs, an owner view of "shared *by* me" aggregated
  across collections, and surfacing group membership in the editor.
