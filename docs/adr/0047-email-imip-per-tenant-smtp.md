# 0047 — Email iMIP via per-tenant SMTP

## Status

Accepted (2026-07-25).

## Context

Scheduling (ADR 0031/0045) is tenant-internal: a recipient without a local schedule-inbox is
dropped. To invite **external** attendees (or reply to an external organizer), the server must send
the iTIP message **by email** — iMIP (RFC 6047). Each tenant uses its own mail server, so the SMTP
configuration is **per-tenant**, set by the tenant admin.

## Decision

- **Per-tenant SMTP config** — a 1:1 shared-PK `TenantEmailSettings` table (Enabled, Host, Port,
  UseStartTls, Username, `PasswordEncrypted`, FromAddress, FromName). The SMTP **password is stored
  reversibly-encrypted** with ASP.NET **Data Protection** (purpose-scoped) so it can authenticate to
  the server. Tenant-admin management: `GET`/`PUT /api/admin/email-settings` (the password is
  **write-only** — GET reports only `hasPassword`) via `ITenantEmailSettingsService`, surfaced as an
  **Email (SMTP)** form on the **Admin** tab.
- **Outbound iMIP** — `SchedulingService` delivery is unified: a **local** recipient → schedule-inbox
  (as before); an **external** recipient → an iMIP email via `IEmailSender.SendItipAsync` **when the
  tenant has SMTP enabled**, else logged/dropped exactly as before. This applies to organizer
  `REQUEST`/`CANCEL` **and** attendee `REPLY` to an external organizer (auto-apply still runs only
  when the organizer is local).
- **`IEmailSender`** is implemented with **MailKit** (MIT) — a `multipart/alternative` of a text part
  and the `text/calendar; method=…` iTIP payload; SMTP `From` = the configured sender, `Reply-To` =
  the human organizer/attendee. `ItipCalendar.Inspect` now surfaces the summary for the subject.

## Consequences

- With SMTP configured, a tenant can invite and correspond with external/cross-tenant attendees.
- **Data Protection keys must be persisted in production** (like the OIDC certs, ADR 0018) for a
  stored password to survive restarts; dev uses ephemeral keys, so the password must be re-entered
  after a dev restart (the decrypt fails soft → treated as no password).
- A **"send test email"** button (`POST /api/admin/email-settings/test { to }` → `IEmailSender.SendAsync`
  plain email using the saved config, ignoring the `Enabled` flag so it verifies *before* enabling;
  SMTP failures are surfaced to the admin) confirms the config end-to-end.
- **Deferred:** **inbound** iMIP (receiving replies *by email* → parsing → delivering to an inbox,
  which needs an IMAP/webhook ingestion pipeline); DKIM/SPF alignment guidance.
