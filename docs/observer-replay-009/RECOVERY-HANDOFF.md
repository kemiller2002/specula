# Rehearsal 009: continuation handoff at the historical stopping point

The observer generated this handoff from the evidence visible at **2026-11-04T02:25:01Z (T+115)**, local visibility:
- 14 visible commits;
- 44 visible CI runs.

The builder's final report is sealed from the observer and was not used. Machine-readable versions: `tests/observer-replay/009/results/handoff-local.json` and `handoff-remote.json`, both validated **consistent** (no violations).

**This is an experiment.** The Rehearsal 009 builder was not restarted, and nothing was written to the Rehearsal 009 repository.

## Clock: do not restart it

| | |
|---|---|
| Original T0 | **2026-11-04T00:30:01Z**, from `evidence/t0/T0.md` at `8001e7c`, recorded before `conditor init` |
| Original deadline | **2026-11-04T07:30:01Z**. It does not move. |
| Remaining at hand-off | **305 minutes** |

- Do not record a new T0.
- Do not restart the seven-hour budget.
- Report all times against the original T0.

## Current state

| | |
|---|---|
| Current repository commit | `ade66fc9fa5225923ce1f3a0f894f5acb8608ac9` (branch `claude/causa-investigation-r9-6l5isz`; `main` at `0e76050` merges it) |
| Completed functionality | The investigation-content application: start an investigation (INV-001..006, 019..023), workspace observations, hypotheses, evidence and links (INV-007..014, 024..031, 038), conclusions (INV-032..033), editing with history (INV-034..036), save and reload from GitHub (INV-015..018, 037); a live GitHub round trip exercised by CI (28 application writes to `causa-data`); 20 of 244 planned v4.1.2 code files implemented |
| Verified requirements | 36 catalog requirements (INV-001, 002, 004..028, 030..038), corroborated by CI run 41754817534 (`33d072d`). INV-003 is recorded not applicable. That is 32 of the 32 applicable mandatory catalog requirements |
| Frozen v4.1.2 plan | **0 of 161** P0 requirements verified: no Verification gate run has passed |
| Remaining mandatory requirements | **161**, all frozen-plan P0. By area: uncategorized 46, agent-reliability 30, ui 30, integration-testing 19, ui-visualization 14, data-boundary 13, dependency-management 9 (the full list is in the JSON) |
| Unmet design contracts | FL-SHELL, FL-07, FL-08, FL-09, FL-TOKENS; Folio not integrated |

## Current release-gate blockers

1. **Verification gate (development) is blocked.** Run-suites refused at `5ef2908`; at earlier SHAs, `SU-OWN-DOTNET produced no result file`. Build and test passes at the same commit. This is a verification-infrastructure failure, not proof of an application defect, but no frozen-plan requirement can be verified while it persists.
2. **Verification gate (release) is blocked.** At `13e1cf8`, `SU-OWN-DOTNET`, `SU-BROWSER-WEBKIT` and `SU-DOTNET-LIVE` produced no result file.

## Highest-priority next actions

1. Do not restart the clock, and do not discard the existing application. Continue from the current commit on a new branch.
2. Open the Verification gate artifact's suite `run.log`. For each refused suite, decide whether:
   - the plan's command is broken: record it and ask the owner for a decision; do not edit frozen inputs; or
   - the plan's test projects are missing: write them.
3. Begin the never-cut plan tasks that are past their latest start, in this order:
   1. T12 invariant tests (15 min);
   2. T11 stack lock (15 min);
   3. T13 semantic kernel and policy/capabilities (45 min);
   4. T18 provider conformance (35 min);
   5. T16 application shell, loading, routes and visual identity (35 min);
   6. T19 save-state machine (60 min).
4. Adopt the Forma patterns the layout contract names in `web/index.html`: shell, navigation, status lozenge with freshness, skeleton, fault banner. Keep the existing `data-requirement-id` bindings so catalog verification stays green.
5. Build the report preview and Folio export (T26); Folio is a required component.
6. Re-run Requirement verification after every merge, so the 32 of 32 applicable mandatory catalog result does not regress.
7. Register the continuation as a Praxis work item before the first commit, and record a checkpoint with the next action.

## Preserve existing work

- Keep every commit on `main` and the `claude/*` branch. Never force-push, reset or rewrite history.
- Keep the catalog verification manifest, its pin and the requirement-implementation map. Extend them; do not replace them.
- Keep the `causa-data` branch; it holds the live round-trip records.
- Do not edit frozen requirement packages, governance-checked workflows or approval records.

## Validation

`Handoff.validate` checked the handoff against the snapshot it was made from, and it passed. It requires that:

- T0 and the deadline equal the authoritative clock, and the deadline equals T0 + 420 minutes;
- the current commit was visible at the hand-off instant;
- every listed verified requirement has visible, CI-corroborated evidence;
- verified, not applicable and remaining together account for every mandatory requirement (194), with no overlap;
- area counts sum to the remaining count;
- remaining minutes match the deadline;
- the clock and preservation instructions are present.

A test also constructs a handoff that restarts the clock and confirms that validation rejects it.

**What a replacement would still lack** (OBS-008, "partial"):
- A continuation record from the builder: HANDOFF.md is still the bootstrap template, and there is no Praxis checkpoint.
- The builder's own reason for treating the catalog as the whole scope. That reason is only in the sealed final report, which the observer could not read.

This handoff fills the first gap from the evidence. It cannot fill the second.
