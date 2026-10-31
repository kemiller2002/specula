# Specula Experience and Visual Design Specification v0.2

Status: design requirements, not implementation. The purpose is to eliminate discretionary visual and interaction decisions that caused rehearsal rework.

## 1. Product character

Specula is an engineering control room for observing autonomous software delivery. The interface should feel calm, legible, precise and evidence-led. It is NOT a generic analytics dashboard, chat application, agent command console or GitHub clone.

Visual hierarchy:
1. Which repository and SHA am I observing?
2. Is the current HEAD built and verified?
3. What is blocked or uncertain?
4. What changed recently?
5. Where can I inspect original evidence?

Do not use celebratory colors or 'all green' banners unless the displayed scope is explicitly verified. Use neutral appearance for unknown, amber for warning/stale/partial, red for verified failure, green only for evidenced success. Labels must always accompany color.

## 2. Design system

- Use the Echelon Foundry palette and released Forma design tokens. Do not invent a second brand palette or hard-code per-screen colors.
- System typography with clear hierarchy: page title, status heading, card heading, body, metadata. Use Forma typography tokens, not arbitrary pixel sizes.
- Consistent spacing using Forma tokens; mobile horizontal padding should be compact but preserve breathing room.
- Card surfaces use consistent borders, corner radius, internal padding and shadow policy. Avoid deeply nested card-on-card compositions.
- Status components have accessible icon + text; color alone is insufficient.
- Default to a clean light/dark adaptive theme using supported Forma tokens. Respect OS preference; manual theme toggle is optional, not a dependency.
- Use restrained transitions; respect reduced-motion settings. Never animate progress without new evidence.
- Charts are optional; status, source links and timestamps are more important than decorative visualizations.
- Dates shown in viewer's local timezone with clear timezone indication; original UTC timestamp available in details.

## 3. Responsive composition

Mobile widths: 320, 375, 390, 430 CSS px. Tablet 768. Desktop 1024 and 1440. Test 200% browser text scaling and mobile safe areas.

Mobile shell:
- Top row: Specula identity; repository switcher (second row if needed); refresh icon/button with accessible label.
- Current repository must remain visible in the shell, abbreviated safely but fully available via accessible label or detail.
- Primary navigation: four destinations Overview, Activity, Verify, Replay. Prefer a bottom bar only if it does not conflict with mobile browser controls or safe area; otherwise use a compact top tab bar.
- One-column content; status cards may use two columns only when labels remain readable at 320 px and enlarged text. Collapse to one column as necessary.
- One natural page scroll; avoid horizontal page scroll and nested vertical scroll.
- Sticky controls must never obscure content, keyboard focus or bottom-most source links.

Desktop shell:
- Same information architecture; use sidebar or top navigation.
- Overview may use two columns: current health and blockers left, activity right.
- Do not introduce desktop-only capabilities.

Long content:
- All containers must allow flex/grid children to shrink (min-width:0 equivalent).
- Wrap long URLs, SHAs, branch names, file paths and commit titles; clamp only with a discoverable full-text detail.
- Explicitly test overflow within rounded cards, buttons, tabs, badges and modal/dialog content.
- Avoid tables on mobile; render stacked semantic records. Wide tables require bounded inner horizontal scroll only.

## 4. Repository selection and remembered state

First run:
- Explain 'Observe a public GitHub repository. Specula does not change it.'
- Label input 'GitHub repository'; placeholder 'owner/repo'.
- Accept owner/repo or canonical github.com repository URL.
- Primary action 'Observe repository'; secondary optional recent-public-repository chips.
- Inline validation for malformed input; API errors for not found/inaccessible/rate limited are distinct.
- On successful selection, store versioned preferences in localStorage. Do not store credentials.

Return visit:
- Restore selected repo immediately from localStorage and show Overview.
- Show 'Connecting...' or 'Refreshing...' until current GitHub data is received. Do not label restored data as fresh.
- Refresh targets the selected repo; it must never reopen the picker.
- Switcher available on every screen; changing repo supersedes outstanding requests and clears previous repo-specific evidence.
- 'Forget repository' clears saved selection and returns to picker.
- If localStorage is disabled or corrupt, continue with in-memory session state.

Preference schema example (normative fields, not an API contract):
```json
{
  "version": 1,
  "selectedRepository": "kemiller2002/specula",
  "recentRepositories": ["kemiller2002/specula"],
  "selectedView": "overview",
  "autoRefreshSeconds": 0
}
```
Do not persist fetched evidence in MVP. Default auto refresh OFF; manual refresh always present. If enabled later, enforce conservative intervals and rate-limit backoff.

## 5. Overview: what is happening right now?

Top:
- Page title 'Overview', repo identity, default/selected branch, HEAD short SHA (link to commit).
- Primary status strip 'Current HEAD checks': Passing / Failing / Running / Unknown. Show checked SHA, source and observation time. Never aggregate old SHA success into HEAD status.
- Secondary strip 'Evidence coverage': Supported / Partial / Not found / Unavailable. This is distinct from CI status.
- If there is an active blocker or failing current check, place it above routine metrics.

Body:
- Four compact metrics, only when grounded: recent commits, open PRs, current HEAD check runs, verified requirements. Each metric has scope and observation time; absent totals say 'Unavailable'.
- 'Needs attention' card shows failing current checks, stale evidence, API limits, unverified blockers. Prioritize failures, then blockers, then stale/unknown.
- 'Recent activity' displays up to five timestamped events with type icon, short title and source link. 'See all activity' opens Activity.
- 'Build window' optional: elapsed, remaining, planned checkpoints and observed evidence for competition mode. Time is based on a configured kickoff, not guessed from first commit.
- Footer: last successful refresh, partial-fetch notice and GitHub API budget if known.

Tapping any metric opens its underlying filtered records. No metric may be a dead-end decorative tile.

## 6. Activity: what changed?

- Reverse-chronological feed, default 'All'; type filters: Commits, Pull requests, Checks, Deployments.
- Each event: type, title, timestamp, outcome (when applicable), SHA and source link.
- Event detail: source record ID, event time, observed time, SHA, actor attribution as supplied by GitHub, related PR/run/commit links when available.
- Distinguish GitHub user attribution from AI-agent identity. Do not label a commit as an agent decision without explicit structured evidence.
- Bounded 'Load more' pagination with visible coverage; do not imply exhaustive history.
- Refresh retains selected filter and, when feasible, scroll position.
- If no events: show 'No activity found in the loaded window' rather than 'Nothing happened'.
- Grouping by day is optional; avoid hiding event timestamps.

## 7. Verify: is the result supported?

Header:
- 'Verification' with current HEAD SHA and status.
- Current HEAD checks and workflow runs, each with source, conclusion, run time and link.
- Show unknown when no current HEAD check exists.

Sections:
- 'Failures and blockers' first. Distinguish failing CI, recorded verification failure, unmet requirement, and inaccessible evidence.
- 'Requirements': verified, failed, blocked, unverified, unavailable. Show source schema/version and coverage scope. Requirement detail includes ID, title, current supported status, evidence link, source SHA/time.
- 'Decisions and corrections': list only recorded decisions and evidenced failure-to-fix sequences. Display 'No supported decision evidence found' when absent.
- 'Evidence sources': list discovered supported files, schema versions, parse errors and missing inputs.
- Unsupported evidence is not silently dropped; show a precise explanation and continue with ordinary GitHub checks.

Never derive 'application works' from commit count, green checks on unrelated SHAs, or requirements files alone.

## 8. Replay: what happened during the build?

- Start/end datetime controls with explicit timezone and validation; no inferred competition start.
- Optional Indy Init preset, editable and explicitly described as a configuration, not an observed fact.
- Chronological event feed within selected window, filterable by type.
- Planned checkpoint lane separate from observed evidence lane.
- Milestone detail: target, observation, source URL, SHA and confidence/coverage caveat.
- 'Presentation view' condenses timeline into current HEAD, meaningful failures and fixes, and final verification with links.
- Show note: 'Specula is pre-existing observation infrastructure; it did not build this application.'
- Never imply a commit timestamp proves local authoring time or full AI reasoning history.

## 9. Interaction details

- Manual Refresh: immediate visual acknowledgment, disable duplicate submissions during in-flight fetch, maintain current repo/view/filter, show last attempt and last success separately.
- Switch repo: confirm only if there is unsaved view-specific input; otherwise switch immediately and isolate data.
- Open source: GitHub link with meaningful accessible label, safe validated URL.
- Errors: actionable human-language message, Retry button when useful, retained selection and clearly marked stale/partial data.
- Loading: stable card skeletons or labeled progress without layout jumps. Respect reduced motion.
- Keyboard and VoiceOver: logical heading structure, descriptive button names, visible focus, status announcements without repetitive chatter.
- Touch targets at least 44x44 CSS px where practical.

## 10. Acceptance and visual regression matrix

Required screenshot and browser checks:
- widths 320/375/390/430/768/1024/1440; both supported themes; 200% text scaling;
- repo names of 1, 40 and 100+ characters, long SHA/branch/commit titles, empty state;
- active refresh, failed refresh, stale state, API 403/429, malformed evidence, unsupported schema;
- no horizontal viewport overflow, clipped text, overlapping navigation, hidden controls or broken rounded corners;
- VoiceOver/keyboard flows for select, refresh, switch, filter, inspect, replay;
- fresh reload restores repo, repeated refresh does not lose selection;
- switch repo during in-flight request never displays stale previous-repo records.

A screen is not accepted on unit tests alone: it requires a browser render at target widths and direct inspection or automated geometry assertions. Store screenshots and pass/fail evidence in Specula's own CI artifacts.
