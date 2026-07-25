# Microsoft Outlook — interoperability gap analysis

How well can Microsoft Outlook sync calendars and contacts with SimplCalCon, and what would it take
to close the gaps? This is a companion to the [DAV client matrix](dav-client-matrix.md), which tracks
the clients that speak CalDAV/CardDAV natively (Apple, DAVx⁵, Thunderbird). **Outlook is different: it
does not speak CalDAV or CardDAV at all.**

## TL;DR

- **No Outlook variant natively supports CalDAV or CardDAV.** Outlook syncs calendar/contacts over
  Microsoft's own protocols (Exchange MAPI/RPC, EWS, Exchange ActiveSync, Microsoft Graph). None of
  those is something a third-party server can realistically implement, and none is on SimplCalCon's
  roadmap.
- **SimplCalCon's DAV surface is already standards-compliant**, so the practical, zero-server-work
  path is the third-party **Outlook CalDav Synchronizer** add-in (classic Outlook for Windows).
- The realistic gaps to *optionally* close on our side are lightweight and read-only-ish:
  a **tokenized ICS subscription feed** (so Outlook's built-in "Internet Calendars" can show a
  calendar read-only) and keeping **iMIP email invitations** solid (already built — this is how an
  Outlook user accepts a meeting today).
- Implementing EWS / ActiveSync / Graph to make Outlook a first-class two-way client is **out of
  scope** — very large, partly proprietary, and low return versus the add-in.

## How each Outlook variant syncs

| Outlook variant | Mail | Calendar | Contacts | CalDAV / CardDAV? |
|---|---|---|---|---|
| **Classic Outlook for Windows** (Win32) | Exchange / IMAP / POP | Exchange, or read-only **Internet Calendar Subscription** (ICS/webcal URL) | Exchange only | ❌ (add-in only) |
| **New Outlook for Windows** | Exchange / IMAP | Exchange / added accounts; ICS subscription | Exchange / connected accounts | ❌ |
| **Outlook for Mac** | Exchange / IMAP | Exchange (older builds had limited CalDAV via macOS Internet Accounts, removed) | Exchange | ❌ |
| **Outlook mobile (iOS/Android)** | Exchange / IMAP | Exchange | Exchange | ❌ |
| **Outlook on the web / Outlook.com** | web | Exchange; ICS subscription (read-only) | Exchange | ❌ |

The one meaningful built-in hook for a third-party server is the **Internet Calendar Subscription**
(a.k.a. "Add calendar → From internet"): Outlook periodically fetches a **read-only** `.ics` URL
(`http(s)://…` or `webcal://…`). There is no contacts equivalent.

## What SimplCalCon offers today

- **CalDAV / CardDAV** at `/dav` (RFC 4791/6352) — full two-way sync for clients that speak it.
- **ICS / VCF export** — `GET /api/{calendars|address-books}/{id}/export` returns a one-shot
  `text/calendar` / `text/vcard` document (authenticated: OIDC bearer, or DAV Basic app-password).
- **iMIP email** (ADR 0031/0047/0056) — the server emails `METHOD:REQUEST/REPLY/CANCEL` invitations
  to external attendees and ingests inbound replies. An Outlook user with no DAV account still
  receives meeting invitations and can Accept/Decline from their inbox.
- **REST `/api`** — a JSON surface, but not something Outlook consumes.

## The gaps and how to close them

Ranked by value-for-effort.

### 1. Outlook CalDav Synchronizer add-in — **recommended, no server work**

[Outlook CalDav Synchronizer](https://caldavsynchronizer.org/) is a mature, open-source (GPL) add-in
for **classic Outlook for Windows** that gives two-way CalDAV/CardDAV sync against any compliant
server. Because SimplCalCon's DAV surface is already standards-compliant, this works **today** with an
app password.

- **Server gap:** none expected. Action is **verification + documentation**: add a setup section to
  [`docs/manual.md`](manual.md) (server URL `https://…/dav/`, app-password auth, autodiscovery via
  `/.well-known/caldav` + `/.well-known/carddav`) and add an "Outlook (CalDav Synchronizer)" column to
  the [client matrix](dav-client-matrix.md) to track acceptance.
- **Limitations:** classic desktop Outlook only (not new Outlook, Mac, mobile, or web); the user must
  install an add-in; some advanced iCal properties may round-trip imperfectly (track in the matrix).

### 2. Read-only ICS subscription feed — **built (ADR 0069)**

Outlook's built-in "Internet Calendars" can subscribe to a calendar for a **read-only**, always-current
view without any add-in — and it works on more Outlook variants (incl. web/Outlook.com).

- **Built:** a revocable **capability-token feed** — `GET /api/calendars/{id}/feed/{token}.ics`
  (`text/calendar`) and `.../address-books/{id}/feed/{token}.vcf` (`text/vcard`) — anonymous (the token
  is the credential), reusing `IObjectImportExport.ExportAsync`. The owner enables/resets/disables it
  from the calendar's Edit modal, which shows the copyable `https`/`webcal` URL (ADR 0069).
- **Trade-offs:** one-way (Outlook can't edit); the URL is a shareable secret (per-collection,
  rotatable, revocable); the poll interval is Outlook-controlled (often hours).

### 3. iMIP invitations — **already built, keep solid**

For *scheduling* (not full calendar sync), an Outlook user is already reachable: the server iMIP-emails
invitations and processes replies (ADR 0047/0056). This covers the most common cross-org need ("invite
my colleague who uses Outlook") without any Outlook-side setup.

- **Server gap:** none functionally; treat as a supported path and include Outlook in iMIP acceptance
  testing (DKIM/SPF alignment for deliverability is the main real-world risk — noted in ADR 0056).

### 4. Native Outlook protocols (EWS / ActiveSync / Graph) — **out of scope**

Making Outlook a first-class two-way client without an add-in would mean implementing a Microsoft
sync protocol server-side:

- **EWS** (Exchange Web Services, SOAP) — huge surface, effectively reverse-engineered against Outlook
  quirks; deprecated by Microsoft in favor of Graph.
- **Exchange ActiveSync** — licensed protocol; not appropriate for an OSS server.
- **Microsoft Graph / connected accounts** — Graph is a *client* API into Microsoft 365; it does not
  let a third-party server present itself to Outlook as an account.

None fits SimplCalCon's scope, licensing (Apache-2.0), or effort budget. Explicitly **not planned**.

## Recommendation

1. **Document + verify the CalDav Synchronizer path** (no code) — the supported two-way answer for
   Windows desktop Outlook. Add it to the manual and the client matrix.
2. **Revocable ICS subscription feed** — **built** (ADR 0069): a read-only calendar view that works
   across Outlook variants and doubles as a generic "subscribe" feature for Apple/Google/Thunderbird.
3. **Keep iMIP deliverability healthy** — the zero-setup path for inviting Outlook users.
4. **Do not** pursue EWS/ActiveSync/Graph.

## Summary matrix

| Capability for Outlook | Mechanism | Status | Effort to close |
|---|---|---|---|
| Two-way calendar + contacts (Win desktop) | CalDav Synchronizer add-in | Works now; **undocumented/unverified** | Docs + acceptance test |
| Read-only calendar (all variants) | ICS subscription feed | **Built** (ADR 0069) | — |
| Meeting invitations (all variants) | iMIP email | **Built** (ADR 0047/0056) | Deliverability testing |
| Native two-way without add-in | EWS / ActiveSync / Graph | **Not planned** | Very large / out of scope |
