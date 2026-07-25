# ADR 0056 — Inbound iMIP (email → scheduling)

## Status

Accepted — implemented. Completes the ADR 0047 deferred "inbound iMIP".

## Context

Outbound iMIP (ADR 0047) delivers REQUEST/REPLY/CANCEL to external attendees by email, but the
reverse — an **external** organizer inviting a local user, or an external attendee replying — had
no path in. SimplCalCon runs no mail server, so the open question was **how the mail reaches the
app**. The processing itself reuses the existing scheduling machinery.

## Decision

A shared **`IInboundItipProcessor`** fed by **two transports** (both opt-in).

- **Processor** (`InboundItipProcessor`, Infrastructure). MimeKit parses the raw RFC822 message and
  extracts the `text/calendar` part; routing keys off the iTIP **content** (not the untrusted
  envelope), resolving addresses to local **active** users across tenants:
  - **REQUEST** → deliver to each local **attendee's** schedule-inbox (they respond via the
    existing `/invitations` UI, which sends a REPLY back out via ADR 0047).
  - **CANCEL** → deliver to each local attendee's inbox **and** soft-delete the matching event
    (by UID) from their calendars.
  - **REPLY** → apply the external attendee's PARTSTAT to the local **organizer's** copy
    (auto-apply, mirroring `SchedulingService`).
  Reuses `IScheduleInboxRepository` + `IObjectStore` + `ItipCalendar` (new `ReadMethod`).
- **REST ingestion endpoint** — `POST /api/inbound-imip` takes the raw message; machine-to-machine,
  authenticated by a shared secret in `X-Inbound-Key` (config `SimplCalCon:InboundEmail:ApiKey`,
  constant-time compared); **disabled (404) when unset**. An operator wires their MTA pipe or an
  inbound-email webhook (SendGrid/Mailgun/Postmark) to it.
- **IMAP poller** — `ImapInboundPoller` (`BackgroundService`, MailKit) polls each tenant's
  configured mailbox for unseen mail, feeds it to the processor, and marks it `\Seen`. **Off by
  default** (`SimplCalCon:InboundEmail:PollerEnabled`); per-tenant + per-message errors are isolated.

### Schema

Extended the existing 1:1 **`TenantEmailSettings`** table (which already holds outbound SMTP) with
inbound IMAP columns: `InboundEnabled`, `ImapHost`, `ImapPort`, `ImapUseSsl`, `ImapUsername`,
`ImapPasswordEncrypted` (**Data-Protection-encrypted**, distinct protector purpose), `ImapFolder`.
No new table. Migrations both providers. Tenant admins configure it in an **Inbound (IMAP)**
section of the existing Email settings form (password write-only, like SMTP).

No new NuGet dependency — MailKit/MimeKit were already in the tree for outbound SMTP.

## Consequences

- The external-scheduling loop is complete: an external organizer's invitation lands in the local
  attendee's invitations; an external attendee's reply updates the local organizer's event.
- Both transports share one processor, so behaviour is identical however the mail arrives.

## Simplifications / deferred

- Routing trusts the iTIP addresses to name local users; there's no per-message DKIM/SPF check
  (the caller — MTA or authenticated IMAP mailbox — is trusted).
- REQUEST for a recurring series / per-occurrence RECURRENCE-ID is delivered as-is (the inbox holds
  the full object); no partial merge.
- The poller has no per-tenant schedule or backoff beyond a global interval; large mailboxes fetch
  all unseen each cycle.
