# Rehearsal 009 observer replay data

| Path | What | Read by the blind replay |
|---|---|---|
| `raw/ci-runs.json` | GitHub workflow runs (trimmed API response) | via the timeline |
| `raw/ci-logs.json` | Verbatim tails of failed jobs' logs | via the timeline |
| `scope-manifest.json` | Authoritative scope from Indy-init `bc3c381` (`specula-observer scope`) | yes |
| `timeline.json` | Read-only reconstruction of Rehearsal 009 (`specula-observer collect`) | yes |
| `results/checkpoints/` | One immutable record per evaluation and mode; digests in `INDEX.json` | written by it |
| `results/alerts-*.json`, `handoff-*.json`, `compute.json` | Replay outputs | written by it |
| `reveal/` | Ground truth from the final report, and the alert adjudication | **no**: only `evaluate` and the leakage test read it |
| `results/evaluation.json` | Detection performance (`specula-observer evaluate`) | written after the replay |

The final report's content is not in `timeline.json`, only its path, commit and sha256. `ObserverTests` checks this.

See `docs/observer-replay-009/FINAL-REPORT.md` for how to regenerate everything.
