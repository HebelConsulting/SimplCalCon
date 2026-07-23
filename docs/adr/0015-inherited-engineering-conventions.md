# ADR 0015 — Inherited engineering conventions from the sibling project

## Status
Accepted (2026-07-23, spec interview)

## Context
SimplCalCon starts from a CLAUDE.md carried over from SimplArchive. The spec
interview reviewed each inherited convention block for applicability.

## Decision
The following carry over **unchanged** (normative wording lives in CLAUDE.md):

- **Code style & exception discipline**: warnings-as-errors repo-wide via
  `Directory.Build.props` (with the `NU1901`–`NU1904` carve-out and the XML-comment
  `--` trap noted), string interpolation over concatenation, switch expressions,
  the two-level intent-named exception hierarchy for `ApiException` and for domain
  errors, plus the `NoBareApiExceptionTests`-style guard test.
- **ADR process**: every architectural decision is a numbered ADR under
  `docs/adr/`; CLAUDE.md stays lean with pointers; the PR implementing a planned
  piece updates the corresponding docs in the same PR.
- **Deployment**: multi-stage Alpine .NET Dockerfile (non-root `app` user,
  `HEALTHCHECK` on `/health/ready`), one shared `docker-compose.yaml` for Docker
  *and* Podman (portable values only, `restart: unless-stopped`), Kubernetes probes
  against `/health/live` + `/health/ready`.
- **License gate**: Apache-2.0-compatible dependencies only, enforced in CI by
  `nuget-license` against `build/licenses/allowed-licenses.json` with
  version-pinned overrides.
- **Collaboration process**: interview-before-deciding and
  show-schema-before-migrating, as stated in CLAUDE.md.

**Dropped as not applicable** (removed from CLAUDE.md):
- JSON+XML dual content negotiation on REST (superseded by ADR 0009).

## Consequences
- Nothing in this ADR is new machinery; it fixes the baseline so future ADRs only
  document deltas from it.
