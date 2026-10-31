# Specula Engineering Requirements v0.1

Status: proposed. Requirements are not evidence of implementation.

## Architecture
- SPEC-E-001: Production code uses F#, Limen WebAssembly runtime, and Forma UI; Folio only for optional print. No default Bolero.
- SPEC-E-002: Initialize using Conditor's supported F#/Limen scaffold. At implementation start, resolve the newest published stable compatible releases of Limen, Forma, Conditor, Folio (only if needed), and other required Echelon packages; verify against actual release artifacts, then pin exact versions. NuGet for .NET and npm for required browser packages.
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

## Dependency freshness and reproducibility
- SPEC-E-006: Never copy dependency versions from a prior rehearsal or the Echelon marketing website without checking newer published stable releases.
- SPEC-E-007: Generate a dependency baseline manifest with package name, exact selected version, registry/release URL, verification date, compatibility result and exception rationale.
- SPEC-E-008: Use newest compatible stable versions, not unverified branch HEADs. Prerelease use requires documented capability need and passing integration verification.
- SPEC-E-009: Lock direct and transitive dependencies where supported, commit lockfiles and verify a clean reproducible install/build.
- SPEC-E-010: A compatibility failure must be recorded with evidence. Do not silently downgrade; choose the highest verified compatible release and document why a newer release was excluded.
- SPEC-V-010: CI verifies that dependency manifest and lockfiles match the actual build; test against the selected release artifacts, not copied source.
- SPEC-V-011: Before implementation and before release, check for newer stable Echelon component versions and evaluate upgrade risk; do not silently upgrade during an active build.
