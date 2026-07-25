# 0044 — Mutation testing with Stryker.NET

## Status

Accepted (2026-07-25).

## Context

The suite has good line/branch coverage, but coverage only proves code *ran* during a test — not
that a test would *fail* if the behaviour changed. Mutation testing measures that: it introduces
small faults ("mutants") and checks the tests catch them, giving a real signal about assertion
quality.

## Decision

Add **Stryker.NET** as a **local tool** (`dotnet-stryker` in `.config/dotnet-tools.json`;
`dotnet tool restore`, like `dotnet-ef`), configured via `stryker-config.json`.

- **Target:** the logic-heavy core, **`SimplCalCon.Infrastructure`**, driven by the fast
  **UnitTests** project — parsers, filter/occurrence evaluators, the object store, ACL /
  principal-graph, auth hashing/policy, the SSRF guard. This is where mutation testing is fast and
  high-signal.
- **Reporters** html + progress + cleartext; **mutation-level** Standard; **thresholds**
  informational (`high` 80 / `low` 60 / **`break` 0** — the job never fails on score).
- **CI:** a **manual + weekly** workflow (`.github/workflows/mutation.yml`, `workflow_dispatch` +
  Monday cron) runs `dotnet stryker` and uploads the HTML report as an artifact. It **does not gate
  PRs** — mutation testing re-runs the suite per mutant and is far too slow for that.
- **Run locally:** `dotnet tool restore && dotnet stryker`. `StrykerOutput/` is gitignored.

## Consequences

- Real feedback on test effectiveness — the very first (scoped) run already surfaced 2 surviving
  mutants in `SsrfSafeConnect` (68% for that file), i.e. behaviour the tests don't pin down.
- Informational, not blocking, so it never wedges unrelated work.
- **Scope:** `Application` (abstractions/records) and `Domain` (entities) carry little mutable logic
  — add them ad-hoc with `dotnet stryker --project SimplCalCon.Application.csproj`. The **Api**
  (mutating it re-runs the slow WebApplicationFactory integration suite per mutant) and the
  **Client** (Blazor) are deferred.
