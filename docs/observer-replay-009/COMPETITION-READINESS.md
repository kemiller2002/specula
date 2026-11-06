# Is the observer ready for competition use on November 7?

**Verdict: NOT READY for unattended live use. READY as an owner-operated replay and spot-check tool.**

The detection rules are ready. Live collection is not: nothing runs the collector on a schedule during a build, and nothing delivers alerts to the owner. The rules have also been tested on only one rehearsal, by an author who already knew its outcome.

## What is ready

| Capability | Evidence |
|---|---|
| Independent authoritative scope (v4.1.2 P0 + catalog, 199 requirements, 863 obligations, 27 tasks, layout contracts), cached with source digests | `tests/observer-replay/009/scope-manifest.json`, compiled by `specula-observer scope` from Indy-init alone |
| Read-only collection: git reads allowlisted, writes refused before a process starts, no lazy fetch | `ReadOnly.admit`; tests 9a and 9b |
| Deterministic rules OBS-001..008 with uncertainty findings; no model calls | `src/Specula.Engine/Observer/Rules.fs`; 121 unit tests |
| Time-correct replay with prefix invariance; content-addressed checkpoint records | tests 1, 2 and the reproducibility test |
| Deduplicated alerts carrying severity, evidence, why, action, confidence and an intervention flag | `alerts-*.json`; test 7 |
| Continuation handoff with consistency validation | RECOVERY-HANDOFF.md; test 8 |
| Specula observer view, phone and desktop | `#/observer`; `tests/browser/observer.spec.mjs` (390 px and 320 px) |
| Cheap enough to run every few minutes | Collection 1.0 s (193 git reads); 129 evaluations in 0.75 s CPU |

## Essential before November 7

1. **A live collection loop.** Run `specula-observer collect` and `replay`, cut to "now", every 5 minutes against a read-only clone, and fetch CI runs and the logs of failed jobs with a read-only token. Today the CI inputs are captured JSON files. This is the one item without which there is no competition monitoring.
2. **Alert delivery.** A Critical alert must reach the owner's phone (push or message) within one cycle. The observer view is pull-only.
3. **Push visibility.** Record push events (`GET /repos/{repo}/events`) or poll branch heads, so that both "work not pushed for 22 minutes" (seen at T+21..T+43 here) and remote visibility are measured rather than bounded.
4. **Blind validation on a second run.** Replay Rehearsal 008, or a synthetic run, with the rule thresholds frozen first, scored by someone who did not write the rules. The 009 result is subject to hindsight bias (ALERT-EVALUATION.md, "Bias statement").
5. **Task-level progress from files, not ID citations.** OBS-002 overstates "not started" for code that serves a plan task without citing its requirement IDs (the one partially supported alert). Map each v4.1.2 task to its planned files (`analysis/project-structure.json`) and measure stub-to-implemented per task.
6. **Gate artifact reading.** Download the Verification gate artifact and read each refused suite's `run.log`. That would have shown MSB1008 and separated "the plan's command is broken (owner decision)" from "the plan's tests are missing (builder work)" at T+47, instead of naming both.
7. **Positive controls for OBS-007.** Rehearsal 009 had no hidden fields, inert controls or placeholder markers. The rule's ability to find them is untested beyond its extraction functions. Add fixtures with each shortcut.
8. **Multi-builder topology.** The competition plan has domain, persistence, ui, ui-2, integration, coordinator and reviewer roles. The observer currently treats every non-data branch as one builder.
9. **An owner runbook** for each Critical alert: what to say to the builder, and when to start a replacement from the handoff.

## Recommended, not essential

- A Folio check that runs before T26's latest start, as an early warning.
- A token-economics feed: the builder's own compute was unmeasured in Rehearsal 009 (report §15).
- Scope digest pinning in the live loop, so a changed manifest is noticed.

## Operating constraints

- The observer never writes to the builder's repository, never merges, never resets the clock, and never edits requirements.
- An inaccessible service (CI log missing, API rate limit) yields an uncertainty, never a failure.
- Only the owner intervenes. The observer recommends.
