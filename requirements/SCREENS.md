# Specula Screen Contracts v0.1

Status: proposed design requirements. No screenshots or implementation claims. These contracts are intended to eliminate layout and behavior ambiguity before agent implementation.

## Navigation and shell

Mobile-first application with four primary destinations: Overview, Activity, Verification, Replay. Use Forma navigation primitives; no custom CSS positioning if a Forma layout primitive suffices. On narrow viewports, bottom navigation with concise labels and accessible names. On wider viewports, navigation may be a sidebar or top bar. Never show internal Echelon system identifiers as decorative content to end users.

Persistent shell:
- Header: Specula name, currently selected `owner/repo` (truncate with accessible full label), repository switch action, manual Refresh action, refresh state.
- Content: one primary screen with a single-column layout on narrow viewports.
- Footer/metadata: data last updated, API rate-limit information when available, and source provenance.
- A loading state must not cause navigation to disappear or selected repo to reset.
- Long repository names, SHA values, commit subjects and paths must wrap or truncate within the container. No horizontal viewport overflow.

## Screen 0: Repository picker (first run or switch)

Purpose: select a public GitHub repository once.
- Accept `owner/repo` and `https://github.com/owner/repo` input; normalize to canonical owner/repo.
- Explain that only public repositories are supported.
- Show a single primary 'Observe repository' action, validation/error message, and recently selected public repositories when stored.
- On success, persist selection to versioned localStorage and navigate to Overview.
- On failure, do not replace the current saved valid repository.
- 'Forget repository' clears saved selection and shows picker.
- If a saved repo exists, launch directly into Overview and refresh automatically, without showing picker.
- Accessibility: label, helper text, input validation, keyboard submit and focus management.

## Screen 1: Overview (default)

Question answered: What is happening with this build?
- Repository name, default/selected branch, last observed HEAD SHA and link.
- Build status for the current SHA only; unknown when no matching run is found.
- Latest activity summary with timestamp and links.
- Small metrics: commits observed, open PRs, relevant CI runs, recorded verification count; show 'partial' or 'not available' instead of false totals.
- Most recent important events (up to five), each linking to Activity.
- A clearly labeled refresh button, last successful refresh time and stale/rate-limit notice.
- Optional competition preset panel: kickoff, elapsed time, remaining time, checkpoint status, all clearly labeled planned vs observed.
- Empty repo state: explain no observed activity; still display repository metadata and actions.

## Screen 2: Activity

Question answered: What changed, when, and where is the evidence?
- Reverse-chronological event feed; filter by commits, PRs, Actions, deployments and all.
- Each event shows type, human-readable title, timestamp/time zone, SHA when applicable, outcome, and 'View on GitHub'.
- Detail expansion displays source ID, event time, observed time, and attribution confidence/source.
- Paginate or load more within bounded request budget; indicate partial coverage.
- Duplicate events across refreshes must not appear.
- Preserve filter and scroll position when refreshing unless data invalidates them.
- Empty, partial and API-error states are distinct.

## Screen 3: Verification

Question answered: Does the current application actually work, and what evidence supports that?
- Top status: current HEAD SHA, CI state for that SHA, last run time, linked workflow.
- Requirements section: verified / failed / blocked / unverified / unavailable from supported structured evidence.
- Recorded decisions and corrections section: only show evidence-backed decision and failure/fix chains; if absent say 'No supported decision evidence found'.
- Failed checks and blockers shown before successes. Avoid a misleading global green badge when requirements evidence is missing.
- Each result links to originating record or file and identifies parser/schema version where applicable.
- For repos without Echelon evidence, show GitHub CI and explicit 'Structured verification unavailable', not a blank page.

## Screen 4: Replay

Question answered: How did the build progress during a selected period?
- Time range inputs with explicit timezone and sensible start/end validation.
- Optional Indy Init preset with editable kickoff, duration and milestones.
- Timeline in chronological order; filter by event type.
- Each milestone is either 'planned' or 'observed' with a supporting source. Missing evidence stays unknown.
- Provide a concise judge-friendly presentation mode, with direct GitHub source links and visible note that Specula was built before the competition.
- Replay is derived from available GitHub records, not a complete record of agent reasoning or local work.

## Layout rules from rehearsal failures

- Mobile baseline: test 320, 375, 390 and 430 CSS-pixel viewport widths, then tablet and desktop.
- No fixed-width cards, tables or long-text fields that exceed viewport. Prefer stacked cards on mobile; wide tables only behind explicit horizontal scroll within their own container.
- Use min-width:0-equivalent flex/grid constraints, overflow-wrap for long identifiers, and padding/box-sizing consistently.
- Rounded corners and inner content clipping must be tested together; never allow child text to spill beyond a rounded parent.
- Avoid nested scroll containers for the main content and avoid sticky controls covering content or keyboard focus.
- Minimum practical touch targets 44 x 44 CSS pixels; respect safe-area insets and browser text scaling.
- Test VoiceOver labels, focus order, contrast, reduced motion, loading announcements and status changes.
- Treat CSS changes as testable behavior, not visual polish to defer until the end.
- Use existing Forma components and fix reusable Forma layout defects upstream when verified; do not block Specula on an unrelated library redesign.

## Refresh and persistence state machine

Selection: None | Valid(owner,repo) | InvalidStoredValue.
Fetch: Idle | Loading | Fresh(timestamp) | Partial(timestamp,reason) | Stale(timestamp,reason) | RateLimited(retryAfter) | Error(reason).

- On launch: parse saved selection defensively; if valid, display selected repo and request fresh data.
- On manual refresh: retain selected repo and screen, prevent concurrent duplicate refresh requests.
- On repo switch: cancel or supersede in-flight fetches; never render late results from the old repo.
- On API error: keep selection and any already visible data but mark it stale or partial.
- On reload: restore selection and filters where supported, then fetch; do not claim restored activity is live.
- On localStorage failure: use in-memory state and do not crash.
- Persist only public repository identifier, optional recent list, selected view, and refresh preference; no tokens.

## Acceptance scenarios

1. First visit: choose `kemiller2002/specula`, see Overview, reload, return to same repo automatically.
2. Refresh ten times: no re-selection, no duplicated events, no concurrent request storm.
3. Switch from repo A to B during an in-flight fetch: no repo A data shown after switch.
4. GitHub offline/rate-limited: selection persists and state is clearly stale/unavailable.
5. Empty repo and repo without Actions: useful Overview and Verification states.
6. Long names, commit titles, paths, narrow screen and 200% text scaling: no clipping or viewport overflow.
7. VoiceOver and keyboard: navigate all four screens, switch repo, refresh and follow source links.
8. CI green on old SHA but failed/unknown on HEAD: Overview and Verification do not claim HEAD is green.
9. Unsupported evidence schema: show unavailable with explanation and continue displaying GitHub facts.
10. Replay a time window: only supported timestamped events and source-linked milestones are shown.
