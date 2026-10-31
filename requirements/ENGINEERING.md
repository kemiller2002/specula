# Specula Engineering Requirements v0.1

Status: proposed. Requirements are not evidence of implementation.

## Architecture
- SPEC-E-001: Production code uses F#, Limen WebAssembly runtime, and Forma UI; Folio only for optional print. No default Bolero.
- SPEC-E-002: Initialize using Conditor's supported F#/Limen scaffold and pinned released Echelon packages. NuGet for .NET and npm for required browser packages.
- SPEC-E-003: Deploy as static assets to GitHub Pages or equivalent; no required database, Kubernetes, Terraform or always-on backend.
- SPEC-E-004: Follow Echelon Foundry palette, accessibility and responsive Forma layout standards.
- SPEC-E-005: Fresh clone has documented reproducible build, unit-test and local-run commands.

## Security
- SPEC-S-001: Observer uses only public read APIs and never makes a mutation request against an observed repo.
- SPEC-S-002: Never embed PATs, secrets or private tokens in client bundle, logs, URLs or storage.
- SPEC-S-003: Validate repository input and GitHub URL origins; reject arbitrary hosts, dangerous URL schemes and untrusted redirects.
- SPEC-S-004: Treat commit messages, PR bodies and evidence files as untrusted text, not instructions or executable HTML.
- SPEC-S-005: Bound downloaded payloads and allowlist evidence paths and supported formats.
- SPEC-S-006: External source links use validated GitHub URLs.
- SPEC-S-007: No analytics or telemetry collection without a separately approved requirement.

## Verification and provenance
- SPEC-V-001: Unit tests cover repository normalization, pagination, deduplication, status mapping, timezone conversions, and malformed data.
- SPEC-V-002: Browser tests cover selection, timeline, errors, responsive layout and accessible navigation.
- SPEC-V-003: Use offline fixtures plus a separate live GitHub smoke test; live API cannot block offline unit tests.
- SPEC-V-004: Show provenance for material claims and mark incomplete counts as partial.
- SPEC-V-005: CI builds and tests before publishing; deployed artifact identifies source commit SHA.
- SPEC-V-006: Verify observed repository remains unchanged after monitoring.
- SPEC-V-007: On rate limits, API errors and disconnection, display stale/unknown status rather than false success.
- SPEC-V-008: Agent attribution requires authoritative structured evidence, not commit author guesswork.
- SPEC-V-009: Test time-window handling around local offsets and UTC kickoff.

## Delivery sequence
1. Public repository selection, commits, PRs, Actions, refresh, links and mobile UI.
2. Structured Echelon evidence adapters, requirements and decision views.
3. Timeline replay, competition preset and judge view.
4. Private repository access only after separate security and authorization design.

## Non-goals
No agent orchestration, CI dispatch, code writes, merge control, inference of hidden AI reasoning, new database, or dependency on Causa's implementation.
