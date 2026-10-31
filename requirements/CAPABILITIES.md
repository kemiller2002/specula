# Specula Capability and Data Contracts v0.2

Status: proposed. This document defines what Specula can and cannot truthfully report.

## Supported MVP capabilities

CAP-001 Select public repository by owner/repo or GitHub URL, save and restore selection with versioned browser localStorage.
CAP-002 Manual refresh current repository without resetting selection, view, or filters.
CAP-003 Read repository identity, default branch, current HEAD SHA, and repository links.
CAP-004 Read commits with SHA, author as GitHub reports it, timestamp and source URL.
CAP-005 Read pull requests and review events where API endpoints expose them; distinguish opened, updated, merged, closed.
CAP-006 Read workflow runs and check runs, bind status/conclusion to exact head SHA.
CAP-007 Read deployments where publicly accessible; distinguish deployment existence from successful production availability.
CAP-008 Merge, deduplicate and paginate events with source identity and bounded API usage.
CAP-009 Detect and display partial coverage, offline, 404, 403/429, and stale refresh states.
CAP-010 Read supported versioned Echelon evidence files from known allowlisted paths; no guessed parsing.
CAP-011 Show requirements coverage only when source schema provides status and evidence.
CAP-012 Show recorded decisions/corrections only when linked to trustworthy source records.
CAP-013 Replay a user-supplied time window and distinguish planned milestones from observed events.
CAP-014 Provide mobile-friendly, keyboard/VoiceOver accessible, source-linked presentation.
CAP-015 Show provenance: source URL, repository, SHA, event timestamp, observed timestamp and adapter/schema version when relevant.

## Explicit limitations

- GitHub history does not expose an agent's private reasoning, internal planning, or uncommitted local actions.
- GitHub commit timestamps are not proof of when code was authored.
- Passing CI is not proof of deployed application usability or complete requirements satisfaction.
- Missing structured requirements evidence is unknown/unavailable, not zero progress.
- GitHub public API has unauthenticated rate limits and endpoint coverage limits; no guarantee of continuous real-time updates.
- Static GitHub Pages deployment can read public APIs from the browser, but cannot securely conceal a PAT.
- Private repositories, server-side indexing, multi-repo portfolio dashboards, notifications and agent orchestration are outside MVP.

## Data model (conceptual F# discriminated unions)

```fsharp
type EvidenceState =
    | Verified
    | Failed
    | Blocked
    | Unverified
    | Unavailable of reason: string

type FetchState =
    | Idle
    | Loading
    | Fresh of observedAt: System.DateTimeOffset
    | Partial of observedAt: System.DateTimeOffset * reason: string
    | Stale of lastSuccess: System.DateTimeOffset option * reason: string
    | RateLimited of retryAfter: System.DateTimeOffset option
    | Error of reason: string

type EventKind = Commit | PullRequest | Review | Workflow | Check | Deployment | Decision | Requirement

type SourceEvent = {
    Repository: string
    StableId: string
    Kind: EventKind
    Title: string
    SourceUrl: string
    EventTime: System.DateTimeOffset
    ObservedAt: System.DateTimeOffset
    CommitSha: string option
}
```

These are requirements examples, not a claim that these exact source files already exist. Implementation may refine them while preserving states and provenance.

## Adapter boundaries

- GitHub adapter: public REST, normalized records and pagination metadata; never emits unsupported Echelon claims.
- Praxis adapter: only parse known documented requirements/work-item formats. Report schema mismatch and source path.
- Ordo adapter: only read recorded decisions if format is actually present and documented.
- Dokimos adapter: only read recorded verification outcomes if format is present and documented.
- Percepta adapter: optional provenance and semantic evidence only when available in supported format.
- Adapter absence must not prevent basic GitHub activity view.
- Before implementing an Echelon adapter, inspect current released schemas in their source repositories; do not invent paths or assume every repo uses the same format.

## API budgeting and caching

- Use bounded concurrent fetches and pagination; cache only in memory during active session.
- Default manual refresh. Optional automatic refresh must respect API limits and retry-after headers.
- When reloading the page, restore repository preferences from localStorage, but re-fetch activity before calling it fresh.
- A user may press Refresh repeatedly without creating duplicate in-flight requests.
- Switching repository must invalidate or supersede old responses by repository identity and request generation.

## Quality gates

QG-001 Correctness: no false green for current HEAD, no unsupported agent attribution, no unlinked evidence claims.
QG-002 Reliability: refresh/reload/switch/offline/rate-limit scenarios pass.
QG-003 Accessibility: touch targets, keyboard focus, screen reader status, text scaling and contrast pass.
QG-004 Layout: screenshots and geometry checks at specified widths, long text and rounded cards.
QG-005 Independence: no observed repo modifications or requirement to install instrumentation.
QG-006 Reproducibility: F# unit tests, browser tests, pinned dependencies and documented build commands.
