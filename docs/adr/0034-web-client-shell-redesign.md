# ADR 0034 — Web client shell redesign (bottom tabs + ribbon + account box)

## Status
Accepted (2026-07-24). Reshapes the Phase 1 web UI ([ADR 0025](0025-phase1-web-ui.md)).

## Context
The Blazor WASM client used the default template chrome (left sidebar `NavMenu`, a thin
top row). The user asked for a navigation model like the sister project SimplArchive:
**no left menu**, a **bottom tab bar**, a **top ribbon** of contextual buttons, and a
**top-right account box** (DisplayName + round initials avatar with a self-service menu).
Interview outcome: build to that description with our own clean styling (no SimplArchive
source to mirror); Admin tab is role-gated with a minimal real starting point; ribbon
buttons derive from existing per-page actions; the account menu is Sign out + a disabled
Profile placeholder (photo later — see the profile-photo recipe / SimplArchive ADR 0310).

## Decision

**Shell (`MainLayout`).** A flex **column** filling the viewport: a fixed **ribbon** on
top, a **scrollable content region** in the middle (`flex:1; min-height:0; overflow:auto`),
and a **bottom tab bar** as a `flex:0 0 auto` child. This guarantees the tab bar sits at
the bottom on **every** tab regardless of content length (content scrolls inside its
region, it never pushes the bar off-screen) — `html,body,#app` are height:100%. The left
`NavMenu`/`LoginDisplay` are removed.

**Tabs (bottom bar).** Overview · Calendar · Contacts · Configuration · **Admin**
(rendered only when `me.IsAdmin`). Routes are unchanged base paths so sub-pages
(share/trash/history) keep working: Overview `/`, Calendar `/calendars/{id?}`, Contacts
`/address-books/{id?}` — the id is now **optional** so the tab has a landing (auto-selects
the first collection; a **switcher** in the ribbon changes it). Overview lists collections;
clicking one opens its tab.

**Ribbon.** A named section (`SectionOutlet`/`SectionContent`, name `RibbonSection`) each
page fills with its contextual actions (Calendar: switcher · Export · Share · Trash;
Contacts: same; Overview: Data takeout).

**Account box.** Top-right: `me.DisplayName` + a round **initials** avatar; clicking it
opens a menu (identity + role, disabled **Profile**, **Sign out**). Identity comes from
`GET /api/me` (`DisplayName`, `Role`) — no token-claim plumbing.

**Configuration tab (`/configuration`).** Merges app-password management with per-client
setup data driven by `/api/me` (server URL from the browser origin, the user's real
`/dav/...` paths, per-client steps incl. the macOS port/`/dav/` + Contacts-one-book caveat
and the Thunderbird own-cert-store note). Replaces the standalone `/app-passwords` page.

**Admin tab (`/admin`), role-gated.** `AdminController` (`api/admin`, ADR 0006) with
in-code role checks (no reliance on role-claim mapping): **platform admin** → `GET
/api/admin/tenants`; **tenant admin** → `GET /api/admin/users` (own tenant). The page shows
the list appropriate to the caller's role. A starting point — management actions later.

## Consequences
- The tab bar is pinned by flex layout, not fixed positioning, so it can't overlap content
  or float up on short/long pages.
- Adding the profile photo later only swaps the avatar's initials for a fetched-bytes data
  URL + a `hasPhoto` flag on `/api/me` (recipe already captured).
- New admin surface is read-only + minimal; broaden in a later unit (user/tenant
  management, invites, deactivation).

## Deferred
Profile photo upload; richer calendar views (month/week grid); Admin write actions;
per-tab ribbon polish; bUnit component guards for the shell.
