# Implementation checklist

Requirement → implementation → verification. Paths are relative to the repository root. "Unit" = `tests/Specula.Engine.Tests`, "Browser" = `tests/browser/acceptance.spec.mjs` / `layout.spec.mjs` (fixtures), "Live" = `tests/browser/live.github.spec.mjs` (real GitHub).

| Requirement | Implementation | Verification |
|---|---|---|
| SPEC-P-001, SPEC-UI-001, SPEC-UI-018, SPEC-S-003, SPEC-A-003 repository input | `Repo.parse` (owner/repo, github.com URLs; rejects schemes, hosts, invalid names) | Unit `RepoTests`; Browser "malformed and missing repositories" |
| SPEC-P-002, SPEC-S-001, QG-005 read-only | Only `GET` effects are constructed (`Update.httpEffect`); credentials `omit` | Code review: no other `HttpMethod`; final report §13 |
| SPEC-D-006, SPEC-S-002 no token | No token anywhere in the client; CI token used only by the live test's own comparison request | `grep` in final report |
| SPEC-UI-002, SPEC-UI-004, SPEC-G-006, QG-001 SHA-accurate HEAD | `Status.headVerdict` (exact SHA, rerun supersedes), `olderPassingRun` warning, `Status.qualify` (partial CI data never yields Passing) | Unit `StatusTests`; Browser "old successful run", "failed CI on HEAD"; UpdateTests "old green CI" |
| SPEC-G-007 refresh consistency | `RecheckHead` stage; `finalize` marks Partial when HEAD moved | Unit "HEAD moving during refresh" |
| SPEC-G-001, SPEC-UI-021, SPEC-A-010 deployment truth | `Status.truthRows` (CI / deployment workflow / URL / smoke) | Unit "deployment without smoke evidence"; Browser same |
| SPEC-UI-003, SPEC-D-003, CAP-004..008 activity | `Events.of*`, `Events.normalize`, filters, details, load more | Unit `EventsTests`; Browser "activity filters and event details" |
| SPEC-D-004, CAP-008 pagination, dedup, budget | `GitHub.maxCommitPages`, `Events.merge`, bounded stages, ETag `If-None-Match` cache | Unit "repeated merges", "repeated refresh is single-flight"; Browser "repeated refresh" |
| SPEC-D-010, SPEC-A-008 single-flight, supersede on switch | Generation + correlation ids; `cancelHttp`; `currentSnapshot` guard | Unit "switching repository during a fetch"; Browser "switching repository during an in-flight check or fetch" |
| SPEC-UI-011, SPEC-V-007, CAP-009 states | `FetchState` (Idle/Loading/Fresh/Partial/Stale/RateLimited/FetchError) projected in header | Unit rate-limit, offline, empty repo; Browser offline, rate limited |
| SPEC-G-004 rate-limit backoff | `RetryAt` from `x-ratelimit-reset`/`retry-after`; refresh refused until reset | Unit "rate limiting is reported and blocks further refresh"; Browser same |
| SPEC-UI-013..017, SPEC-D-007, SPEC-D-009, SPEC-G-018 persistence | `Preferences` (versioned `specula.preferences`, migration, bounds), Limen Storage effects | Unit `PreferencesTests`, "unavailable or corrupt storage"; Browser reload, new session, corrupt, blocked storage, forget |
| SPEC-G-015 shared links | `?repo=` parsed as session-only, "Make this my repository" | Manual (engine path covered by `parseLocation`) |
| SPEC-D-002, SPEC-G-003, CAP-010..012 evidence adapters | `Evidence.registry` (5 allowlisted paths, exact schema ids taken from a real observed repository) | Unit `EvidenceTests`; Browser "unsupported evidence schema", "supported evidence" |
| SPEC-UI-005, SPEC-UI-006 requirements, decisions, corrections | Verification screen sections | Browser "supported evidence lists requirements, blockers and decisions" |
| SPEC-P-006, SPEC-UI-019..022, SPEC-D-011, SPEC-D-012, SPEC-A-009, SPEC-A-011 three dimensions, metrics | Overview dimensions; `Metrics.all` (`specula.metrics/1`) with definition/denominator/window/provenance | Unit `MetricsTests`; UpdateTests "repository without actions" |
| SPEC-UI-007, SPEC-UI-008, SPEC-G-002, SPEC-G-005, SPEC-A-005 replay, milestones, clock | Replay screen; `Metrics.milestones`, `Metrics.clock`; per-repository competition preferences; recorded kickoff adapter | Unit clock tests; Browser "replay shows a configured window…" |
| SPEC-UI-009 judge view | Presentation view with pre-existing-infrastructure statement | Browser replay test |
| SPEC-G-012 about/provenance | About screen (`BuildInfo` SHA, build time, versions, adapters); `dist/build.json` | Live "deployed build identifies its source commit" |
| SPEC-G-016 settings | Settings screen | Browser forget/replay tests |
| SPEC-UI-010, EXPERIENCE §3/§10 responsive layout | `web/app.css` (Forma tokens only), stacked single column, wrapping identifiers | Browser layout at 320/375/390/430/768/1024/1440, 200% text, light/dark |
| Accessibility (QG-003) | Skip link, landmarks, labelled controls, live status, 44px targets, glyph + text status | Browser "keyboard", "accessibility basics" |
| VISUAL-TOKENS color contract | Only public `--ef-*` tokens | `npm run check:colors` (Forma `validateColorFiles`) |
| SPEC-E-002, SPEC-E-006..E-010, SPEC-V-010 dependencies | `dependency-baseline.json`, exact npm pins + lockfile, vendored attested nupkgs | `npm run check:deps`; `sha256sum -c vendor/nuget/checksums.txt` in CI |
| SPEC-E-003, SPEC-G-013 hosting | Static `dist/`, relative asset paths, versioned asset dirs, hash routing, `404.html` | deployed-smoke job (served SHA, wasm content type, live smoke) |
| SPEC-V-005 CI before publish, SHA in artifact | `ci.yml` build-and-test → live-github → deploy → deployed-smoke | Workflow runs |
| SPEC-S-004 untrusted text | Limen `textContent` only; no HTML from data | By construction (Limen cannot render markup) |
| SPEC-S-007 no telemetry | No analytics; CSP `connect-src` limited to GitHub | `web/index.html` CSP |
