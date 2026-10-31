# Specula Product Requirements v0.1

Status: proposed. No implementation or acceptance claims.

## Purpose and boundaries
- SPEC-P-001: Observe any accessible public GitHub repository supplied as owner/repo or GitHub URL.
- SPEC-P-002: Remain read-only: no commits, merges, workflow dispatch, approvals, or agent control in observed repositories.
- SPEC-P-003: Require no installation or instrumentation in the observed repository.
- SPEC-P-004: Clearly separate observed facts, derived summaries, planned milestones, and unavailable evidence.
- SPEC-P-005: Support any repository, not just Causa or Indy Init.

## User interface
- SPEC-UI-001: Mobile-first repository selector with input validation and useful error messages.
- SPEC-UI-002: Overview shows repository, branch, latest commit, last successful refresh, data freshness, and build status tied to a SHA.
- SPEC-UI-003: Chronological activity timeline shows commits, pull requests, reviews, CI runs, and deployments, each linked to original GitHub evidence.
- SPEC-UI-004: CI view distinguishes queued, running, passed, failed, cancelled, skipped, and unknown; never mistake a prior SHA's passing CI for current HEAD.
- SPEC-UI-005: Requirements view reads existing versioned Praxis/compiled evidence where available; show verified, failed, blocked, unverified and unavailable states.
- SPEC-UI-006: Decisions and corrections view shows documented recommendations, decisions, failures and repairs; do not invent unrecorded reasoning.
- SPEC-UI-007: Replay view filters events by a chosen time window, preserving event timestamps and source links.
- SPEC-UI-008: Competition preset supports explicit kickoff time, deadline, and milestones; distinguish target versus observed progress.
- SPEC-UI-009: Judge view offers concise live demo of progress, corrections and evidence, identifying Specula as pre-existing infrastructure.
- SPEC-UI-010: Responsive Forma UI works on iPhone and desktop without text overflow, inaccessible controls, or clipped content.
- SPEC-UI-011: Empty, loading, partial, stale, rate-limited, offline and error states are visually distinct.
- SPEC-UI-012: Each screen offers links to source records for verification.

## Data
- SPEC-D-001: Consume public GitHub REST API for metadata, commits, PRs, workflow runs/checks and deployments as available.
- SPEC-D-002: Parse Echelon evidence only from explicitly supported versioned schemas, with graceful fallback for unknown schemas.
- SPEC-D-003: Event records include stable source ID, repository, source URL, observed-at time, event time and SHA where available.
- SPEC-D-004: Bound pagination, deduplicate records, show partial coverage, and honor GitHub rate limits/backoff.
- SPEC-D-005: Manual refresh and optional conservative periodic refresh; display refresh time and failures.
- SPEC-D-006: No GitHub token or private repository access required for initial public MVP.
- SPEC-D-007: Persist only non-sensitive user preferences in browser localStorage: selected public owner/repo, optional recent public repositories, active view, and refresh preference. GitHub remains authoritative for live activity. Do not persist tokens, private data, or claim stored results are current.
- SPEC-D-008: Do not infer agent identity from commit author alone or claim that commits prove when code was authored.

## Acceptance
- SPEC-A-001: Load active public repository with correct commits, PRs and SHA-linked CI results.
- SPEC-A-002: Load public repo lacking Actions or Echelon evidence without crashing or fabricating progress.
- SPEC-A-003: Show useful errors for missing, inaccessible and malformed repository inputs.
- SPEC-A-004: Replay a failed check and later fix without misattributing results.
- SPEC-A-005: Show a competition time window with observed versus planned milestones.
- SPEC-A-006: Pass iPhone viewport, keyboard navigation and accessibility checks.

## Persistence and refresh additions
- SPEC-UI-013: Save a valid selected public repository to localStorage after successful selection; restore it automatically on reload or a new browser session, without requiring re-entry.
- SPEC-UI-014: A persistent repository switcher is available on every primary screen; switching updates localStorage and clears previous repository's displayed evidence immediately.
- SPEC-UI-015: Refresh button always targets the currently selected repository, without changing route or selection; preserve current view and filters when practical.
- SPEC-UI-016: On launch, display the restored repository immediately and fetch current data; if offline or rate-limited, show explicit stale/unavailable state, never a false live state.
- SPEC-UI-017: If localStorage is unavailable, corrupted, or blocked, the application remains usable with session-only state and an unobtrusive explanation.
- SPEC-UI-018: Repository selection must be a valid public GitHub owner/repo; support explicit 'Forget repository' and 'Switch repository' actions.
- SPEC-D-009: Store preferences under a versioned namespaced key; validate and migrate known versions or discard invalid data safely.
- SPEC-D-010: Refresh is single-flight per repository, cancellable or superseded on repository change, with bounded requests and rate-limit-aware backoff.
- SPEC-A-007: Select repository, reload, navigate between screens and refresh; verify selection remains and no re-entry is needed.
- SPEC-A-008: Test malformed, unavailable and disabled localStorage, stale network data, repeated refresh taps and switching repository during an in-flight request.
