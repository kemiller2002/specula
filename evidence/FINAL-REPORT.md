# Specula — final report

Date: 2026-10-31. Prepared by the implementing agent; this is a self-report backed by the linked CI runs and deployed-site checks, not an independent acceptance review.

## Verdict: ACCEPTED (against the mission's completion criteria)

Every mandatory completion criterion in the mission brief has passing test and deployment evidence (§4, §6–§9). The verdict is scoped: §5 and §11 list requirement-document items that were not verified (real-device Safari/VoiceOver, WebKit, forced colors) and residual risks. If those items count as mandatory, the correct verdict is **PARTIAL**.

## 1. Repository and commits

- Repository: https://github.com/kemiller2002/specula (branch `main`)
- PR #1 `Build Specula…` → merge commit `45829b771bf8fa48b2159aab99f2d40f8b8efe83`
- PR #2 `Fix system dark theme, deploy-job detection and metric word breaks` → merge commit `99aa755ecd6e2a0b8af29637c8c390a1caa8ea89`. This is the last application build that was deployed and smoke-tested before this report was written (§9).
- PR #3 (this report) also fixes the partial-CI provenance defect D-5. When it merges, the same pipeline rebuilds, redeploys and smoke-tests that merge commit. Its `build.json` and About screen identify that SHA.

## 2. Published URL

- https://specula.echelonfoundry.com/ (GitHub Pages custom domain; `https://kemiller2002.github.io/specula/` → 301 to it)

## 3. Dependency versions (resolved 2026-10-31; see `dependency-baseline.json`)

| Component | Version | Source |
|---|---|---|
| Limen browser kernel `@echelon-foundry/limen` | 0.9.1 (latest) | npm |
| Limen F# `EchelonFoundry.Limen.Contract` / `.Guest` | 0.9.1 | Sigstore-attested GitHub release assets, vendored, SHA-256 verified in CI |
| Forma `@echelon-foundry/design-system` | 0.13.0 (latest; tag v0.13.0) | npm; Echelon brand `dist/brands/echelon.css`, color contract `forma.color-contract/1.0.0` |
| .NET WebAssembly | net10.0, SDK 10.0.x (`global.json`), `wasm-tools` | Microsoft |
| `@playwright/test` (tooling only) | 1.63.0 | npm |

Not used, with reasons recorded in the baseline: Folio (optional; not on npm), Conditor 0.10.18 (its scaffold provisions a whole governed environment for an empty repository; its layout conventions were followed instead), Praxis, Ordo, Dokimos and Percepta (not runtime dependencies; their formats were inspected before the adapters were written).

## 4. Implemented requirements

Full traceability: `evidence/IMPLEMENTATION-CHECKLIST.md`. Summary:

- **Architecture**: all logic is in F# (`src/Specula.Engine`, pure transitions and projection) behind Limen. `Program.cs` is a one-method `[JSExport]` shim. UI is static HTML with Forma classes; there is no second UI framework and no backend.
- **Screens**: picker; Overview (HEAD, SHA-bound checks, evidence coverage, needs attention, separate **Activity / Verification / Efficiency** sections each linking to evidence, observed counts, build window); Activity (filters, details, load more); Verification (HEAD checks, build/deploy-workflow/deploy-URL/smoke rows, blockers first, requirements, acceptance ledger, decisions, corrections, efficiency definitions, evidence sources); Replay (window, planned versus observed milestones, competition clock, presentation view); Settings; About (source SHA, build time, versions, adapters, diagnostics).
- **GitHub reliability**: unauthenticated public `GET` only. Bounded staged refresh (repo → commits/PRs/runs/deployments → HEAD checks, deployment statuses, evidence → HEAD recheck), generation-guarded, single-flight, and superseded on repository switch. ETag `If-None-Match` caching, rate-limit backoff from `x-ratelimit-reset`/`retry-after`, and partial-coverage labelling.
- **Persistence**: versioned `specula.preferences` in `localStorage` (selected/recent repositories, last view, theme, per-repository competition clock). Migration from an unversioned shape; corrupt, blocked or full storage falls back to the session. No credentials or GitHub data are stored.
- **Correctness rules**: HEAD status only from records with the exact HEAD SHA (reruns supersede). An old green run is shown as "does not verify HEAD". Passing results from partial CI data are reported as Unknown (D-5). Unknown is never styled as success. Missing evidence is reported as unavailable, never as zero. Kickoff is never inferred from commits.
- **Evidence adapters**: five allowlisted paths with exact schemas taken from a real observed repository (`causa.traceability/1`, `causa.pat-ledger/1`, `indy-init.rehearsal-kickoff/1`, decision and defect markdown tables). Any other schema version is reported as unsupported.
- **Metrics** (`specula.metrics/1`): time to first passing CI, time to first evidenced deployment, failure-to-recovery, verified requirements per hour, correction cycles. Each states its definition, denominator, window and provenance, and returns "Not measurable from available evidence" when evidence is insufficient.

## 5. Deferred or blocked requirements

| Item | Status | Reason |
|---|---|---|
| VoiceOver on a real iPhone, Safari safe-area on a device | Not verified | No Apple device in this environment. Automated Chromium checks cover labels, focus order, live status and 44 px targets; `viewport-fit=cover` and `env(safe-area-inset-*)` are implemented but not observed on hardware. |
| WebKit/Firefox browser runs | Not run | Only Chromium is available here and in CI. |
| Forced colors, color-vision simulation | Not tested | Status always pairs color with a glyph and text label, which mitigates this. |
| Verified requirements per hour | Implemented as "not measurable" | Supported evidence records no per-requirement verification timestamps. |
| Praxis/Ordo/Dokimos/Percepta native evidence | Not supported | The observed repository publishes `causa.*`/`indy-init.*` formats; no released on-disk format from those systems was found in an observed repository, so none was guessed. |
| Cross-tab preference sync | Not implemented | Each tab reads preferences at start. |
| Exports, PWA, optional polling (P2) | Deferred | Optional; outside core acceptance. |
| Conditor scaffolding | Not used | Recorded exception (§3). |

## 6. Unit-test results

F# xUnit, `tests/Specula.Engine.Tests`:

- **58/58** passed in CI on PR #1 (run 41192820130, job `build-and-test`).
- **59/59** in CI on PR #2 (run 41193809241).
- **62/62** locally with this report's changes.

Areas covered: repository normalization and hostile input, GitHub parsing and status mapping, rate-limit/404/409 classification, Link pagination, SHA binding (old green, reruns, case and prefix), deployment truth, deduplication and merging, evidence parsing and unsupported schemas, metrics and the clock, storage migration and corruption, viewer-zone time handling around a UTC kickoff, and the full refresh state machine via a fake host (switch mid-fetch, single-flight, offline, rate limit, empty repo, HEAD moved, partial CI).

## 7. Browser-test results

Playwright (Chromium) with deterministic GitHub fixtures, `tests/browser/acceptance.spec.mjs` and `layout.spec.mjs`:

- **30/30** passed in CI on PR #1.
- **31/31** on PR #2, and again locally with this report's changes.

Covered: first selection, reload and new-session restore; repeated refresh without duplicates or concurrent refreshes; switching during an in-flight check and during an in-flight fetch; failed CI; old green with a new unverified HEAD; no Actions; deployment without smoke evidence; unsupported and supported evidence; offline; rate limit; malformed and missing repositories; corrupt and blocked storage; forget; replay, milestones and presentation; activity filters, details and source links.

Layout: no horizontal overflow, no content escaping cards and no clipped controls on every screen at 320/375/390/430/768/1024/1440 px with very long repository, branch and commit names; 200% text; light and dark themes actually applied (background luminance asserted); explicit theme override; keyboard (skip link, navigation, refresh); labelled controls, one `h1`, live status and 44 px targets.

## 8. Live integration-test results

`tests/browser/live.github.spec.mjs` uses real public GitHub data and no fixtures. The CI token is used only for the test's own Node-side HEAD comparison, never by the app.

- `live-github` job against the local build: passed on PR #1, `main` 45829b7, PR #2 and `main` 99aa755.
- Locally from this sandbox, the first live run loaded real `kemiller2002/specula` data and restored it after reload. Later runs hit GitHub's unauthenticated rate limit, which the app reported with its reset time and without losing the selection.

## 9. Deployment verification

The `deployed-smoke` job on `main` 45829b7 (run 41193086813) and on `main` 99aa755 passed. It:

- polls `https://specula.echelonfoundry.com/build.json` until `sourceSha` equals the commit;
- checks that `dotnet.native.*.wasm` is served as `application/wasm`;
- opens the deployed site in Chromium and selects `kemiller2002/specula`;
- asserts that the displayed HEAD equals the API's HEAD, that commits, pull requests and checks appear with `https://github.com/kemiller2002/specula/...` links, and that the selection and screen survive reload without page errors;
- asserts no horizontal overflow at 390 px on all four primary screens.

Independent check from this session after the deploy: `build.json` served `99aa755…`, with `limen 0.9.1`, `forma 0.13.0`. The `.wasm` content type was `application/wasm`, and the root returned 200. In a 390 px browser, Specula observing itself showed HEAD `45829b7`, then `99aa755`, with its own `build-and-test`, `deploy` and `deployed-smoke` checks. It rendered "Deployment URL available: Recorded" and "Application smoke test: Reported passing — Specula did not run it".

## 10. Screenshots

- `evidence/screenshots/*-390-light.png`: deployed build 99aa755 observing `kemiller2002/specula`, all screens at 390 px, light theme.
- `evidence/screenshots/*-1440-dark.png`: deployed build 99aa755 at 1440 px, dark theme. This capture happened while GitHub's secondary rate limit refused this sandbox's check-runs and workflow-runs requests. It therefore shows the degraded state: **Partial**, HEAD **Unknown**, never passing. It also exposed defect D-5, fixed in PR #3.
- CI artifacts: `specula-test-evidence` (Playwright report, layout screenshots at 320/390/1440 for every screen, light and dark), `specula-live-evidence`, and `specula-deployed-smoke-evidence` (live screenshots of the deployed site) on each run, for example https://github.com/kemiller2002/specula/actions/runs/41193086813.

## 11. Known defects

Found and fixed during delivery:

| ID | Defect | Found by | Fix | Guard |
|---|---|---|---|---|
| D-1 | Observe button disabled while a repository was being checked, so users could not switch mid-check | browser test | button stays enabled; a new submit supersedes the check | "switching … during an in-flight check or fetch" |
| D-2 | Primary navigation overflowed at 200% text | browser layout test | navigation wraps | "200% text scaling" |
| D-3 | System dark theme never applied (`data-ef-theme="system"` blocked Echelon's media rule) | inspecting the deployed site; the original test checked contrast only | theme attribute moved to a wrapper; test asserts dark luminance | layout dark-theme test (failed on old code) |
| D-4 | "Deployment workflow: Not found" despite a passing `deploy` job | inspecting the deployed site | job check runs are matched too; smoke jobs excluded | unit test (failed on old code) |
| D-5 | When check runs or workflow runs could not be read, the text said "no runs were found" and run-based metrics read as observed. A passing workflow run plus unread check runs could have shown HEAD as Passing. | inspecting the deployed site under a live secondary rate limit | `Status.qualify` downgrades Passing to Unknown; unreadable-source wording; run-based metrics become not measurable | unit tests including a fake-host refresh with rate-limited check runs |
| D-6 | "Unavailable" broke mid-word in metric cards at desktop widths | deployed-site screenshot | smaller Forma type step | visual check |

Open:

- Smoke-test and deployment-workflow detection match check and workflow **names** (`smoke`; `deploy`/`pages`/`release`/`publish`). This is labelled in the UI as name-based and "Specula did not run it". A repository that names things differently shows "Not verified" or "Not found", never a false pass.
- "Requirements evidence" milestone time is the HEAD commit time; the first appearance is not determined. The UI states this.
- FSharp.Core trim warnings (IL2104) in the WebAssembly publish. No runtime failure has been observed in the browser suites.

## 12. Remaining risks

- **GitHub unauthenticated limit**: 60 requests per hour per IP. A refresh costs about 12 requests; 304 answers are free but the cache is in memory only. Shared NAT or corporate IPs can exhaust the limit; the app then shows Rate limited or Partial and blocks refresh until reset. This was observed live from this sandbox.
- Bounded windows: 50 runs per branch, 30 PRs, commits up to 5 pages, 100 check runs. All are labelled as partial when truncated.
- Live checks depend on GitHub availability.
- The deployed-smoke and live tests observe `kemiller2002/specula` itself. That is a real, active repository, but it is not the competition target.

## 13. Evidence that observed repositories were not modified

- Code: the only HTTP method the engine constructs is `HttpMethod.Get` (`src/Specula.Engine/Update.fs`, `httpEffect`). Requests use `credentials: "omit"` and no token. The page CSP restricts `connect-src` to `'self' https://api.github.com https://raw.githubusercontent.com`.
- `kemiller2002/indy-init-rehearsal-007` HEAD was `f61493fa04f896977bb099c15870912a6990194d` when inspected before implementation and is unchanged after delivery (`git ls-remote`, 2026-10-31).
- The Echelon component repositories (limen, forma, folio, conditor, praxis, ordo, dokimos, percepta) were only cloned anonymously. This session had no write access to them.
- `kemiller2002/specula` was changed only through PRs #1–#3 by the implementing agent. The application itself never writes to it.

## 14. Recommended next improvements

1. Run the browser suite on WebKit, and run VoiceOver and safe-area checks on a physical iPhone.
2. Use a structured smoke-test evidence format, for example a versioned `evidence/smoke.json` written by the observed repository's deploy job, instead of name matching.
3. Persist ETag bodies in `sessionStorage` (public data only) so reloads cost 304s rather than full requests, or add an optional user-supplied token held in memory only, after a security review.
4. Add per-requirement verification timestamps to `causa.traceability` so "verified requirements per hour" becomes measurable.
5. Add adapters for released Praxis, Ordo and Dokimos evidence once those formats appear in observed repositories.
6. Folio printable report and JSON/CSV export (P2).
