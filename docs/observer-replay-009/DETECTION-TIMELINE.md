# Rehearsal 009 observer replay: detection timeline

All times are UTC, on 2026-11-04. T0 is **00:30:01Z** and the deadline is **07:30:01Z** (420 minutes). Both come from `evidence/t0/T0.md` at the bootstrap commit `8001e7c`; the deadline equals T0 plus the budget, and the collector refuses a T0 record where it does not.

Machine-readable sources:

| Content | File |
|---|---|
| Timeline | `tests/observer-replay/009/timeline.json` |
| Checkpoint records | `tests/observer-replay/009/results/checkpoints/{local,remote}/*.json`, digests in `INDEX.json` |
| Alert logs | `tests/observer-replay/009/results/alerts-{local,remote}.json` |

## Visibility modes

The replay ran twice, with different rules for when a commit becomes visible:

- **Local:** the observer sees each commit at its commit time. This is what an observer with read access to the builder's working copy would see.
- **Remote:** the observer sees a commit only once GitHub shows it. Push times were not retrieved, so this mode uses the first CI run on the commit or a descendant. That is an upper bound on the push time.

The remote mode matters because the builder pushed nothing before 01:13:08Z (T+43). Its first five application commits, made from 00:51Z onward, existed only in its working copy for up to 22 minutes.

## What was observable, in order

The table lists builder commits and the CI results that changed a finding. The 32 `causa-data` commits are excluded: they are writes the application under test made during live round-trip tests, recorded in the timeline as `testDataWrites`.

| T+ | Time | Event (repository or CI) | Local alert | Remote alert |
|---|---|---|---|---|
| 2 | 00:32:11 | `8001e7c` T0 bootstrap: Conditor, v4.1.2 skeleton, 6 workflows | – | – |
| 21 | 00:51:17 | `edde38c` content application: F# domain, GitHub persistence, Limen engine, `web/index.html`, `web/causa.css` | **W** OBS-003 design contract (5 layout contracts unmet); **W** OBS-006 no Praxis work item; **W** OBS-008 recovery insufficient; I Limen boundary file | not visible |
| 29 | 00:59:15 | `6ee2281` probes; `catalog-pin.json` and the verification manifest compiled from CAT-CAUSA-INVESTIGATION alone | **W** OBS-001 declared scope is the catalog only | not visible |
| 37 | 01:07:19 | `8e663bd` negative scenarios | **W** OBS-002 schedule (T12 past latest start) | not visible |
| 40 | 01:10:03 | `f1e0be7` mobile and conclusion suites, fault probes | – | not visible |
| 42 | 01:12:50 | `1f2206b` CI workflows; `work.started R9-CONTENT-001` at 01:12:37, baseline `f1e0be7` | **W** OBS-006 registered 21 min late (evidence changed); I OBS-002 no CI yet (uncertainty) | not visible |
| 43 | 01:13:08 | First CI runs; the branch is pushed. `ececa70` installs the SDK pins | I OBS-005 Build and test failed, cause unknown (uncertainty) | **W** OBS-001, **W** OBS-002, **W** OBS-003, **W** OBS-006, all at once |
| 47 | 01:17:59 | Verification gate at `ececa70`: `REFUSED suite SU-OWN-DOTNET produced no result file` | **W** OBS-005 gate blocked (infrastructure, not an application defect) | same |
| 58 | 01:28:39 | Requirement verification at `ececa70`: a content fault probe was not detected | **W** OBS-005 fault probe survived; **W** OBS-002 schedule, 4 tasks | same |
| 63 | 01:32:59 | `33d072d` independent-review fixes | – | – |
| 77 | 01:47:49 | Requirement verification at `33d072d` succeeds | I resolved: fault probe | same |
| 78 | 01:48:36 | `296b245` evidence: 36/37 verified, accepted | **C** OBS-001 narrow catalog verified (32/32 applicable mandatory) while 0 of 161 frozen-plan requirements are verified; I OBS-002 verified count | same |
| 81 | 01:51:35 | `39dc037` merges PR #1; Deploy fails (exit 155, SDK not installed) | **W** OBS-005 deploy toolchain | same |
| 84 | 01:54:06 | Build and test at `39dc037` succeeds | **W** OBS-002 schedule, 6 tasks | same |
| 87 | 01:57:15 | Release-mode gate at `39dc037`: 3 suites produced no result file | **W** OBS-005 release gate blocked | same |
| 93 | 02:03:25 | `5ef2908` deploy SDK fix | – | – |
| 97 | 02:07:06 | Gate at `5ef2908` refuses; the cause line is not in the collected tail | W evidence changed (cause unconfirmed) | same |
| 98 | 02:08:20 | Deploy at `13e1cf8` succeeds | I resolved: deploy | same |
| 104 | 02:14:09 | Release-mode gate at `13e1cf8` refuses (3 suites) | – (deduplicated) | – |
| 111 | 02:22:00 | `ade66fc` adds `evidence/FINAL-REPORT.md` (sealed: the observer sees the path only) | **C** OBS-004 completion declared with 161 mandatory requirements unverified and 308 min remaining | **C** at T+112 |
| 115 | 02:25:01 | Historical stopping point | handoff generated (RECOVERY-HANDOFF.md) | same |
| 150 | 03:00:01 | No builder commit since `0e76050` (02:25:49) | **C** OBS-004 stopped: 34 idle min, 270 min remaining | same |

W = Warning, C = Critical, I = Informational. Every alert, with its evidence, is in the alert logs.

## The fixed checkpoints

| Checkpoint | Local HEAD | Code-committed / verified (194 mandatory) | Active W+C | Recovery | Remote HEAD |
|---|---|---|---|---|---|
| T+0 | – | 0 / 0 | 0 | insufficient | – |
| T+20 | `8001e7c` | 0 / 0 | 0 | insufficient | – |
| T+40 | `8e663bd` | 32 / 0 | 5 | insufficient | – (nothing pushed) |
| T+60 | `ececa70` | 32 / 0 | 6 | partial | `ececa70` |
| T+80 | `296b245` | 32 / 32 (catalog only) | 6 | partial | `296b245` |
| T+100 | `13e1cf8` | 32 / 32 | 7 | partial | `13e1cf8` |
| T+115 | `ade66fc` | 32 / 32 | 8 | partial | `ade66fc` |
| T+150 (extension) | `0e76050` | 32 / 32 | 9 | partial | `0e76050` |

Two checkpoints had insufficient evidence, and their records say so:

- T+0 and T+20 had no builder commit beyond the bootstrap.
- T+40 in remote mode had no pushed commit at all.

From T+29 onward, progress in code committed stayed flat at 32 catalog requirements and 0 of the 161 frozen-plan requirements. Meanwhile 28 of 50 CI runs and 28 live round-trip writes accumulated. Activity grew without the frozen plan's scope moving, which is the "satisfactory progress" pattern the observer was built to see through.

## Evaluation points

The replay made 129 evaluations: 66 local and 63 remote. That is the 8 fixed checkpoints plus every commit and every non-cancelled CI completion. Each evaluation read only `Replay.snapshotAt(mode, instant)`.

The tests check this directly. At every checkpoint, the findings computed from the full timeline are identical to those computed from a timeline with everything after that instant deleted.
