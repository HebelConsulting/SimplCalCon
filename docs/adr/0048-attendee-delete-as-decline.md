# 0048 — Attendee delete as decline

## Status

Accepted (2026-07-25).

## Context

RFC 6638 auto-scheduling (ADR 0031/0045): an **organizer** deleting an event sends `CANCEL`, but an
**attendee** deleting their copy of an invited event did nothing — a scheduling no-op. Removing an
invitation should tell the organizer you're not coming.

## Decision

- `SchedulingService.ProcessDeleteAsync` now branches on the actor:
  - **organizer** delete → `CANCEL` to every attendee (unchanged);
  - **attendee** delete → a `REPLY;PARTSTAT=DECLINED` to the organizer, via the unified
    `DeliverAsync` (organizer's schedule-inbox if local, else an iMIP email per ADR 0047) plus
    **auto-apply** of the DECLINED status to the organizer's local copy.
- **REST delete now schedules too:** `EventsController.Delete` calls `ProcessDeleteAsync` (mirroring
  the DAV object controller), so deleting an invited event from the **web** also declines it.

## Consequences

- Removing an invitation — over DAV or the web — notifies the organizer that the attendee declined.
- **No schema change.**
- **Deferred:** per-instance decline for a single occurrence of a recurring series; distinguishing a
  deliberate "decline" from a "remove from my calendar" (both map to DECLINED here).
