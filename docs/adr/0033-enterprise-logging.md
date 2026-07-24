# ADR 0033 — Enterprise logging (Serilog, structured, level-graded)

## Status
Accepted (2026-07-24).

## Context
The service had only the ASP.NET Core default console logger with ad-hoc levels. For
an operable multi-tenant DAV/REST server we want **structured** logs (queryable
key/value properties, not string soup), a **consistent severity grading** so operators
can filter by "does this need me?", and per-request visibility — without drowning the
signal (DAV clients poll `/health` and sync constantly).

Interview outcome: **Serilog** (the de-facto enterprise structured-logging library;
Apache-2.0, so it clears the license gate — [ADR 0009]), a **structured console** sink
(human-readable in Development, compact JSON elsewhere for container/cluster log
collectors), and instrumentation at the **key seams + request pipeline** rather than
every method.

## Decision

**Six levels, graded by operator intent** (the project-wide principle, mirrored in
CLAUDE.md). Serilog's names map 1:1 to the requested semantics; `ILogger` callers use
the Microsoft `LogLevel` in parentheses:

| Level (Serilog / `ILogger`) | When |
|---|---|
| **Trace** / `LogTrace` | Most verbose; *may clutter the log*. Full payloads (blobs, bodies), per-item detail. Off outside deep debugging. |
| **Debug** / `LogDebug` | Verbose but *no clutter*. Normal control-flow milestones: a write stored, a message delivered, auth succeeded, a skip taken. |
| **Information** / `LogInformation` | *Clear, no clutter.* One line per meaningful outcome: request summary, a scheduling REQUEST/REPLY/CANCEL, bootstrap steps. |
| **Warning** / `LogWarning` | *High probability an admin must act.* Degraded-but-serving conditions (e.g. an invited admin needing activation). |
| **Error** / `LogError` | *An exception for an admin to investigate.* Unexpected server faults (5xx); always with the exception. |
| **Fatal** / `LogCritical` | *Service impaired.* Startup/host failure — the process cannot serve. |

**Stack.** `Serilog.AspNetCore` (bundles the console sink, compact-JSON formatter, and
`appsettings` binding). A **two-stage** init in `Program.cs`: a bootstrap logger before
the host is built (captures construction failures), then the real logger configured from
`IServiceProvider` + the `Serilog` configuration section, enriched with `FromLogContext`
+ an `Application` property. Sink is a human template in Development, `CompactJsonFormatter`
otherwise. Levels live in the `Serilog` config section (`Microsoft*`/`System`/EF Core
default to Warning to kill framework/SQL noise); Development floors at Debug.

**Request pipeline.** `UseSerilogRequestLogging` emits one structured summary per request
(method, path, status, elapsed, `UserId`, host). `/health/*` probes drop to Debug so
readiness polling doesn't bury the signal; any exception or 5xx is Error.

**Fatal on startup.** The whole host build+run is wrapped; anything escaping (except the
`HostAbortedException` that `WebApplicationFactory` throws to capture the host) is logged
at **Fatal** and returns a non-zero exit, then `Log.CloseAndFlush()`.

**Seam instrumentation (key seams, not every method).**
- `ProblemDetailsExceptionHandler`: 5xx → Error (with exception); handled 4xx → Debug.
- `BootstrapHostedService`: migrations + completion → Information; already-seeded → Debug;
  invited admin (activation needed) → Warning.
- `DavBasicAuthenticationHandler`: success → Debug; failure → Information (never the
  secret).
- `ObjectStore.PutAsync`: the blob → Trace; the stored write (op, resource, change no.) →
  Debug.
- `SchedulingService`: each iTIP REQUEST/REPLY/CANCEL → Information; per-recipient
  delivery / no-local-recipient → Debug.

**DAV wire trace (`DavWireTraceMiddleware`, Trace).** A first-class use of the Trace level:
an operator can log the full `/dav` request/response bodies (method, path, depth, status +
raw XML/blob, both CalDAV and CardDAV) to diagnose a native client without attaching a
reverse proxy. It is **off by default** and gated on `IsEnabled(Trace)` for the
`SimplCalCon.Dav.Wire` category, so it is a pass-through with no body buffering unless
enabled (e.g. `Serilog__MinimumLevel__Override__SimplCalCon.Dav.Wire=Verbose`). Because it
captures contact/calendar payloads and clutters the log, the **first** trace entry per
process also raises a one-time **Warning** that tracing is active and should be turned off —
an intentional Warning-means-act signal.

## Consequences
- Application code logs through `ILogger<T>` (Serilog is only the provider), so nothing
  is coupled to Serilog except `Program.cs`; sinks (file, Seq, OTLP) can be added by
  configuration later.
- Compact JSON in production is machine-parseable; Docker/K8s capture stdout (no log
  files to manage — [ADR 0024]).
- New code follows the level table above; reviewers treat a mis-graded log (e.g. an
  expected 4xx at Error, or a noisy Info in a hot loop) as a defect.

## Deferred
Correlation/trace IDs beyond the per-request scope, additional sinks (file/Seq/OTLP),
log-based metrics, and audit logging (distinct from diagnostic logging).
