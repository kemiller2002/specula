# Specula Requirements Gap Closure v0.1

Status: proposed requirements. This is a gap assessment against PRODUCT.md, ENGINEERING.md, SCREENS.md, EXPERIENCE.md, CAPABILITIES.md and VISUAL-TOKENS.md, not an implementation claim.

## P0: Competition-critical behavior

- SPEC-G-001 Deployment truth: show independently 'Build/CI', 'Deployment workflow', 'Deployment URL available', and 'Application smoke test'. A green build or deployment workflow must never imply the deployed application was opened and tested. If there is no independent smoke-test evidence, say 'Not verified'.
- SPEC-G-002 Progress model: define a bounded set of milestone types (repository initialized, first build, first tests, first deployment, requirements evidence, acceptance). Each milestone must be Planned, Observed with source, or Unknown. No invented percentage complete or velocity.
- SPEC-G-003 Evidence discovery: define a versioned adapter registry listing supported source path patterns, schema identifiers, versions, parsing limits, provenance requirements, and failure states. Discover only allowlisted sources; do not scan an entire repo indiscriminately. Inspect actual released Praxis, Ordo, Dokimos, and Percepta contracts before writing adapters.
- SPEC-G-004 API budget: specify a request budget per manual refresh, ETag/conditional requests where supported, bounded pagination, progressive loading and user-visible remaining budget. Respect GitHub unauthenticated limits and 403/429 retry guidance. No background polling by default.
- SPEC-G-005 Competition clock: user-configured local kickoff, deadline, timezone, and checkpoints persisted as public non-sensitive preferences. Compute elapsed and remaining from a trusted device clock with explicit caveat that the device clock may be wrong. Show deadline passed without implying development stopped. Never infer kickoff from commit time.
- SPEC-G-006 Branch and SHA integrity: observe selected branch explicitly; distinguish default-branch changes, force pushes, rebases, workflow reruns, pending checks, missing check suites, and delayed/out-of-order API events. Every status belongs to a precise SHA and observation time.
- SPEC-G-007 Refresh consistency: fetch repository identity and HEAD, load related evidence, then recheck HEAD or otherwise mark snapshot potentially inconsistent if it changed mid-refresh. Display partial snapshot rather than combining statuses from different SHAs.
- SPEC-G-008 First-run and degraded usability: useful experience for empty public repos, disabled Actions, no deployments, absent Echelon evidence, network offline, API failure, and rate limits. Never crash or show a global success from missing data.
- SPEC-G-009 End-to-end acceptance: use a controlled public fixture with deliberately mismatched CI SHA, failed then rerun checks, missing schema, deployment without smoke test, and repository switch during refresh. Tests must verify displayed conclusions and provenance.

## P1: Operational and analytical value

- SPEC-G-010 Engineering metrics: define measures and denominators before implementation. Candidate metrics include time from configured kickoff to first successful HEAD CI, first evidenced deployment, failure-to-recovery duration, verified requirements per hour, and percentage of observed events with provenance. Label partial observation and avoid cross-run comparisons unless workloads and windows are comparable.
- SPEC-G-011 No fabricated agent cost: token use, model identity, agent attribution, and dollar cost are unavailable unless supplied by a supported structured source. Never estimate from commit volume or wall-clock time.
- SPEC-G-012 About screen: display Specula version, deployed source SHA, build timestamp, pinned Limen/Forma versions, adapter versions, license and repository link; make accessible from persistent shell without adding a fifth primary tab.
- SPEC-G-013 Hosting: define GitHub Pages base-path routing, asset URLs, cache busting, 404 fallback, secure public API origin, source-map publication policy and deployment rollback. Verify fresh browser load and hard refresh on a deep link.
- SPEC-G-014 iPhone home-screen use: provide installable PWA metadata/icons only if feasible without service-worker complexity. App must work as a normal mobile webpage regardless. If service worker is added, specify update lifecycle and stale-data messaging.
- SPEC-G-015 Navigation and deep links: define shareable read-only URL parameters for repository and screen (optional), with precedence over saved local preference and explicit user confirmation before changing the saved default. Validate all inputs. No secret-bearing URL parameters.
- SPEC-G-016 Settings: separate lightweight settings sheet/page for repository, branch, time zone, competition preset, refresh preference, theme (if supported), recent repositories and 'Forget repository'. Keep settings off primary navigation.
- SPEC-G-017 Observability of Specula: visible last attempted refresh, last successful refresh, partial endpoints, fetch errors and retry guidance. Optional diagnostics panel exposes safe request counts and adapter outcomes without tokens or private data.
- SPEC-G-018 Browser storage behavior: specify schema migration, maximum recent-repo count, corrupted data fallback, cross-tab changes, and clearing all saved settings. Never store credentials or downloaded GitHub evidence.
- SPEC-G-019 Comparative evidence: version event and metric definitions so future rehearsal comparisons are meaningful; do not compare partial windows as if complete.

## P2: Valuable extensions, not competition blockers

- SPEC-G-020 Export: downloadable source-linked JSON/CSV or printable Folio report for current window with data provenance and coverage warnings; no backend required.
- SPEC-G-021 Optional polling: conservative opt-in refresh with visible state, pause on background tabs when appropriate, obey rate limits and avoid exhausting unauthenticated quota.
- SPEC-G-022 Accessibility polish: validate mobile Safari VoiceOver in addition to automated browser checks; test large text, forced colors, reduced motion, focus restoration after dialogs, and live status announcements.

## Requirements integrity and delivery controls

- SPEC-G-023 Resolve contradictions across documents before implementation: SCREENS and PRODUCT define persistent selection, while any older 'in-memory only' language must mean GitHub evidence, not preferences.
- SPEC-G-024 Provide one canonical feature-to-screen-to-adapter-to-test traceability matrix; each P0 has an owner, fixture, browser assertion and evidence path.
- SPEC-G-025 Implementation order: (1) real public GitHub data + selection persistence + manual refresh; (2) SHA-accurate CI and deployment truth; (3) mobile UI and browser acceptance; (4) evidence adapters; (5) replay/presentation; (6) optional analytics/export. Preserve a functioning vertical slice after each stage.
- SPEC-G-026 Freeze contract: after resolving P0 ambiguities and dependency versions, changes require a recorded reason and impact assessment. Do not silently expand scope mid-build.
- SPEC-G-027 Verification boundary: unit tests prove parsers and state transitions, browser tests prove rendered behavior, and live smoke tests prove external integration. None substitutes for the others.

## Open decisions to resolve before freezing

1. Which deployed-app smoke-test evidence, if any, will exist in observed repositories? Specula can show it but cannot claim to run it without explicit capability.
2. Should the competition preset be saved per repository or globally? Recommended: per repository.
3. Should the initial branch always follow default branch or allow selection? Recommended: default initially, explicit optional override.
4. Which exact released Echelon schemas are present and stable enough to parse?
5. Which GitHub Pages path will host Specula, and does it need deep-link routing?
6. Is optional polling worthwhile under public unauthenticated GitHub limits? Recommended: manual refresh for MVP.
