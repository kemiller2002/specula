# Rehearsal 009 observer replay: final report

**Result:**

- A read-only, deterministic observer, built into Specula, replayed Indy Init Rehearsal 009 blind, using only evidence that existed at each instant.
- It raised supported alerts for all five known failures. Four of them arrived 21 to 47 minutes after T0. The first **Critical** ("narrow catalog verified, frozen plan 0 of 161") came at **T+78, with 342 of 420 minutes remaining**, and 33 minutes before the builder declared completion.
- It raised no false and no unsupported alerts; one alert is partially supported.
- It produced a validated continuation handoff at the stopping point.

This shows **detection and intervention opportunities**, not a changed outcome: the observer did not run during the real rehearsal, and nobody can say what the builder would have done with its alerts.

The supporting documents are:

| Document | Content |
|---|---|
| [DETECTION-TIMELINE.md](DETECTION-TIMELINE.md) | What was visible when, and the alert raised at each point |
| [ALERT-EVALUATION.md](ALERT-EVALUATION.md) | Every alert adjudicated; detection latency |
| [RECOVERY-HANDOFF.md](RECOVERY-HANDOFF.md) | The continuation handoff at T+115 |
| [COMPETITION-READINESS.md](COMPETITION-READINESS.md) | What is still needed before November 7 |

Machine-readable results are in `tests/observer-replay/009/`:

| Content | Path |
|---|---|
| Timeline | `timeline.json` |
| Authoritative scope | `scope-manifest.json` |
| Checkpoint records (129) and their digests | `results/checkpoints/`, `INDEX.json` |
| Alert logs | `results/alerts-*.json` |
| Handoffs | `results/handoff-*.json` |
| Evaluation | `results/evaluation.json` |
| Compute | `results/compute.json` |
| Specula view data | `web/observer/replay-009.json` |

## The final report questions

**Would the observer have caught Rehearsal 009's scope failure?** Yes.
- At **T+29** (00:59Z), when the builder pinned CAT-CAUSA-INVESTIGATION as the only verification scope, the observer warned that the frozen plan's 161 mandatory requirements were outside it. Its evidence was:
  - 0 of 161 frozen-plan IDs cited by any change;
  - the mission work item `COND-MISSION-001` still `ready`.
- At **T+78** (01:48Z) it raised a **Critical**: the narrow catalog was verified (32 of 32 applicable mandatory) while 0 of 161 frozen-plan requirements were verified, with 342 minutes remaining.
- At **T+111** a second Critical: completion declared with 161 mandatory requirements unverified.

**Would it have caught the CSS regression?** Yes, at **T+21** (00:51Z), the first commit with application markup. It raised a Warning: Forma is imported, but 5 mandatory layout contracts are unmet. Each is named with its exact missing classes:

| Contract | Missing |
|---|---|
| FL-SHELL | `ef-workspace-shell`, `ef-navigation-shell`, `ef-site-header`, `ef-skip-link` |
| FL-07 | `ef-status-lozenge`, `ef-freshness` |
| FL-08 | `ef-skeleton`, `ef-fault-banner` |
| FL-09 | `ef-navigation-shell` |
| FL-TOKENS | 3 literal colours, later 4, outside `var()` fallbacks |

The evidence also named the regression against Rehearsal 008 (`fd8fba9`), which used site-header, navigation-shell, status-lozenge, freshness, conflict-review and skip-link. Custom CSS that only composes tokens was not flagged.

**What was the earliest supported detection point for each?**

| Failure | Local visibility | Remote visibility (GitHub only) |
|---|---|---|
| Scope narrowing | T+29 Warning; T+78 Critical | T+43 Warning; T+78 Critical |
| Forma/CSS regression | T+21 | T+43 |
| Release-gate problems | T+47 (release mode T+87), classified as gate infrastructure, not an application defect | T+47 |
| Praxis sequencing | T+21 (no work item); T+42 (registered 21 min late) | T+43 |
| Premature completion | T+111 Critical; T+150 stop confirmed | T+112; T+150 |

Remote detections wait for T+43 because the builder pushed nothing until 01:13Z. That 22-minute window is itself a recovery-readiness finding.

**What intervention should the owner have made?**
- **At T+29 to T+47:** ask the builder which frozen-plan slices it will execute and when, and whether its catalog-only verification scope was authorized. Also ask it to adopt the Forma shell patterns before more screens are built.
- **At T+47 to T+87:** treat the gate refusal as an infrastructure problem with two possible causes, the plan's suite command or missing plan tests. Have the builder read the gate's `run.log`, and decide as owner on any frozen-input defect, since only the owner may.
- **At T+78:** the Critical alert was the decisive point. Reject "catalog verified" as completion and direct the builder to the never-cut tasks past their latest start (T12, T11, T13, T18, T16, T19), keeping the 07:30:01Z deadline.
- **At T+111:** reject the completion report, and continue with the same builder or a replacement from the observer's handoff, without restarting the clock.

**How much monitoring compute was consumed?**

| Item | Amount |
|---|---|
| Observer at run time | Collection 1.0 s wall, 193 read-only git invocations; replay 129 evaluations in 0.75 s wall / 0.77 s CPU (about 6 ms per evaluation, both modes); **0 model calls** |
| GitHub API (once, for the historical capture) | 1 workflow-run listing, 1 job listing, 11 job-log tails |
| Storage | Timeline 289 KB; scope manifest 351 KB; checkpoint records 3.4 MB |
| Building this experiment | The agent session that built the observer is not metered here: its token totals are not exposed to the agent, so they are left unmeasured rather than estimated |

**What additional monitoring is essential before November 7?** In summary:
1. A live 5-minute collection loop with CI log retrieval.
2. Alert delivery to the owner's phone.
3. Push-event visibility.
4. Blind validation on a second run, with thresholds frozen first.
5. File-based task progress.
6. Reading the gate artifact's `run.log`.
7. Positive controls for functional-shortcut detection.
8. Multi-role topology support.
9. An owner runbook for Critical alerts.

The details are in COMPETITION-READINESS.md.

**Is the observer ready for competition use?** **No, not for unattended live use.** The rules, replay and view are ready, and the observer can be operated today as an owner-run spot check. Live collection and alerting are not built.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Timeline with SHA, time, files, requirements, tests, CI, capabilities, architecture | PASS | `timeline.json`: 15 builder-branch commits, 32 test-data writes, 50 CI runs (11 with log tails), sealed report digest |
| 2 | T0 and deadline from authoritative evidence | PASS | `evidence/t0/T0.md` at `8001e7c`; the collector checks deadline = T0 + 420 |
| 3 | Repository-visible activity distinguished from unobservable work | PASS | `unobservable` list; OBS-000 uncertainty; remote vs local modes |
| 4 | Authoritative scope manifest (IDs, mandatory, priority, area, criteria, verification, evidence, dependencies, contracts) | PASS | `scope-manifest.json`: 161 P0 + 38 catalog requirements, 863 obligations with criteria text, 27 tasks, 5 design contracts |
| 5 | Scope not inferred from the builder | PASS | Compiled from Indy-init only; no 009 file is read by `scope` |
| 6 | Source conflicts reported, not resolved narrowly | PASS | SC-1 Forma 0.12.0 vs 0.13.0; SC-2 CANDIDATE vs frozen; SC-3 OD-INDY-20 wording |
| 7 | Observer in Specula, F#, Echelon architecture, no new orchestration framework | PASS | `src/Specula.Engine/Observer/*` (pure F#), `src/Specula.Observer.Cli` (F#), Limen view, Forma markup |
| 8 | Reuses Limen, Forma, Praxis and Dokimos integrations where appropriate | PASS | Limen effects and Forma components for the view. Praxis `.ros` events and Dokimos `requirement-verification/1` are read as evidence. The Praxis and Dokimos CLIs are not executed (not needed by a read-only observer) |
| 9 | Read-only against the monitored repository | PASS | Allowlist with write refusal; the integration test proves refs, index and files unchanged; GET-only HTTP |
| 10 | OBS-001 scope coverage | PASS | T+29 Warning, T+78 Critical |
| 11 | OBS-002 progress trajectory (code / demonstrated / tests / verified / not evaluated) | PASS | `progress` in every checkpoint. One alert is partially supported (citation-based measure) |
| 12 | OBS-003 architecture compliance with exact contracts | PASS | Five named contracts with missing classes; Limen checked; Folio checked against T26 |
| 13 | OBS-004 premature completion | PASS | T+111 Critical; T+150 stop Critical |
| 14 | OBS-005 verification blockers, infrastructure vs implementation | PASS | Gate classified as suite infrastructure with same-SHA build evidence; toolchain and fault-probe classes; unknown cause yields an uncertainty |
| 15 | OBS-006 Praxis sequencing | PASS | T+21 and T+42 |
| 16 | OBS-007 functional shortcuts only with evidence | INCONCLUSIVE | No false alerts (none present in 009); detection power untested without positive fixtures |
| 17 | OBS-008 recovery readiness at every checkpoint | PASS | `recovery` in each of the 129 records |
| 18 | Blind chronological replay, no future data, no final report | PASS | Prefix-invariance and sealed-content tests; content-addressed records |
| 19 | Significant events between checkpoints evaluated | PASS | 129 evaluation points (commits and CI completions) |
| 20 | Alert fields, three severities, deduplication | PASS | Alert schema; test 7; design-contract warning sent once in 66 evaluations |
| 21 | Detection of scope narrowing, CSS, release gate, Praxis, premature completion | PASS | ALERT-EVALUATION.md |
| 22 | False or unsupported alerts measured | PASS | 0 false, 0 unsupported, 1 partially supported of 17 distinct findings |
| 23 | Monitoring compute measured | PASS | `compute.json`, collector stats. The development session is not metered (stated) |
| 24 | Handoff with every required field, consistent, original deadline preserved | PASS | RECOVERY-HANDOFF.md; `Handoff.validate`; test 8 |
| 25 | Required tests 1–10 automated, on actual historical data | PASS | `tests/Specula.Engine.Tests/ObserverTests.fs`, 20 tests, 121 total passing |
| 26 | Specula observer view with all eight items, Forma, iPhone-sized | PASS | `#/observer`; browser tests at 390 and 320 px; screenshots in `screenshots/` |
| 27 | Historical replay separate from live monitoring; limitation documented | PASS | Separate model state and static data; the view states that live monitoring is not available |
| 28 | No new backend service | PASS | Static file on the existing GitHub Pages site |
| 29 | Live monitoring during the competition | FAIL | Not built (COMPETITION-READINESS.md item 1) |
| 30 | Experiment demonstrates detection early enough to support intervention | PASS | First Critical with 342 minutes left; the warnings that led to it from T+21 |
| 31 | Rehearsal 008, 009 and Indy-init unmodified; other agents not disturbed | PASS | Only clones were read. 009 was attached for API reads only; nothing was pushed outside Specula |
| 32 | Mobile WebKit suite | INCONCLUSIVE | WebKit is not installed in this environment; CI runs `npm run test:mobile` |

## How to reproduce

The full pipeline:

```bash
dotnet build src/Specula.Observer.Cli
OBS="dotnet src/Specula.Observer.Cli/bin/Debug/net10.0/specula-observer.dll"
D=tests/observer-replay/009
$OBS scope    --indy-init ../Indy-init --baseline ../indy-init-rehearsal-008 --out $D/scope-manifest.json
git clone --mirror https://github.com/kemiller2002/indy-init-rehearsal-009 r9.git
$OBS collect  --repo r9.git --scope $D/scope-manifest.json --indy-init ../Indy-init \
              --runs $D/raw/ci-runs.json --logs $D/raw/ci-logs.json --out $D/timeline.json
$OBS replay   --scope $D/scope-manifest.json --timeline $D/timeline.json --out $D/results --web web/observer/replay-009.json
# Only after the replay:
$OBS evaluate --results $D/results --ground-truth $D/reveal/ground-truth.json \
              --adjudication $D/reveal/alert-adjudication.json --out $D/results
```

The tests:

```bash
dotnet test tests/Specula.Engine.Tests
npm run build && npx playwright test tests/browser/observer.spec.mjs
```

The Indy-init source was `bc3c381`. The Rehearsal 009 refs were `main` at `0e76050`, the claude branch at `ade66fc`, and `causa-data` at `e68fe2f`.
