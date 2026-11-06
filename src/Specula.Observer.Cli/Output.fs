namespace Specula.Observer.Cli

open System
open System.IO
open System.Text.Json.Nodes
open Specula.Engine.Observer
open Specula.Engine.Observer.Types
open Specula.Observer.Cli.Common

/// The replay command: run the blind replay in both visibility modes and
/// write immutable, content-addressed checkpoint records, the alert log,
/// the recovery handoff and the data the Specula observer view reads.
module Output =

    let fixedMinutes = Engine.standardCheckpoints
    /// One extension point after the window, to test stop detection with
    /// evidence that existed at that instant (no commits follow).
    let extensionMinute = 150

    let finding (f: Finding) =
        jobj
            [ "rule", jstr f.Rule
              "key", jstr f.Key
              "kind", jstr (match f.Kind with | FindingKind.Observation -> "observation" | FindingKind.Uncertainty -> "uncertainty")
              "severity", jstr (severityCode f.Severity)
              "title", jstr f.Title
              "requirements", jstrs f.Requirements
              "evidence", jstrs f.Evidence
              "why", jstr f.Why
              "action", jstr f.Action
              "confidence", jstr (confidenceCode f.Confidence)
              "ownerInterventionNecessary", jbool f.Intervention
              "fingerprint", jstr f.Fingerprint ]

    let alert (a: Alert) =
        jobj
            [ "id", jstr a.Id
              "at", jtime a.At
              "minute", jint a.Minute
              "change", jstr (alertChangeCode a.Change)
              "severity", jstr (severityCode a.Finding.Severity)
              "rule", jstr a.Finding.Rule
              "key", jstr a.Finding.Key
              "finding", finding a.Finding ]

    let progress (p: Progress) =
        jobj
            [ "elapsedMinutes", jint p.ElapsedMinutes
              "remainingMinutes", jint p.RemainingMinutes
              "builderCommits", jint p.BuilderCommits
              "lastCommit", jopt jstr p.LastCommit
              "lastCommitAt", jopt jtime p.LastCommitAt
              "plannedFilesImplemented", jint p.PlannedFilesImplemented
              "plannedFilesTotal", jint p.PlannedFilesTotal
              "mandatoryRequirements", jint p.MandatoryRequirements
              "frozenPlanMandatory", jint p.FrozenPlanMandatory
              "catalogMandatory", jint p.CatalogMandatory
              "codeCommitted", jint p.CodeCommittedRequirements
              "demonstrated", jint p.DemonstratedRequirements
              "verifiedFrozenPlan", jint p.VerifiedFrozenPlan
              "verifiedCatalog", jint p.VerifiedCatalog
              "notEvaluated", jint p.NotEvaluated
              "testRunsCompleted", jint p.TestRunsCompleted
              "testRunsPassed", jint p.TestRunsPassed
              "liveRoundTripWrites", jint p.LiveRoundTripWrites
              "tasksDue", jstrs p.TasksDue
              "tasksDueUnaddressed", jstrs p.TasksDueUnaddressed ]

    let recovery (r: Recovery) =
        jobj
            [ "readiness", jstr (readinessCode r.Readiness)
              "checks", r.Checks |> List.map (fun c -> jobj [ "item", jstr c.Item; "present", jbool c.Present; "detail", jstr c.Detail ]) |> jarr
              "missing", jstrs r.Missing ]

    let checkpoint (inputDigest: string) (c: CheckpointResult) =
        jobj
            [ "schema", jstr "specula.observer-checkpoint/1"
              "label", jstr c.Label
              "at", jtime c.At
              "minute", jint c.Minute
              "mode", jstr (visibilityCode c.Mode)
              "trigger", jstr c.Trigger
              "inputDigest", jstr inputDigest
              "visibleCommits", jstrs c.VisibleCommits
              "headSha", jopt jstr c.HeadSha
              "progress", progress c.Progress
              "findings", c.Findings |> List.map finding |> jarr
              "alerts", c.Alerts |> List.map alert |> jarr
              "recovery", recovery c.Recovery
              "limitations", jstrs c.Limitations ]

    let handoff (h: Handoff) (violations: string list) =
        jobj
            [ "schema", jstr "specula.observer-handoff/1"
              "generatedAt", jtime h.GeneratedAt
              "t0", jtime h.T0
              "deadline", jtime h.Deadline
              "remainingMinutes", jint h.RemainingMinutes
              "currentCommit", jopt jstr h.CurrentCommit
              "completedFunctionality", jstrs h.CompletedFunctionality
              "verifiedRequirements", jstrs h.VerifiedRequirements
              "notApplicable", jstrs h.NotApplicable
              "remainingMandatory", jstrs h.RemainingMandatory
              "remainingByArea", h.RemainingByArea |> List.map (fun (a, n) -> jobj [ "area", jstr a; "count", jint n ]) |> jarr
              "blockers", jstrs h.Blockers
              "nextActions", jstrs h.NextActions
              "preserve", jstrs h.PreserveInstructions
              "clock", jstrs h.ClockInstructions
              "evidenceBasis", jstrs h.EvidenceBasis
              "validation", jobj [ "consistent", jbool violations.IsEmpty; "violations", jstrs violations ] ]

    /// The highest-priority active observation's action.
    let recommended (findings: Finding list) =
        findings
        |> List.filter (fun f -> f.Kind = FindingKind.Observation && f.Severity <> Severity.Informational)
        |> List.sortByDescending (fun f -> severityRank f.Severity, (if f.Intervention then 1 else 0))
        |> List.tryHead
        |> Option.map (fun f -> f.Action)
        |> Option.defaultValue "No intervention indicated by the available evidence."

    let viewCheckpoint (c: CheckpointResult) =
        let p = c.Progress
        let active = c.Findings |> List.filter (fun f -> f.Kind = FindingKind.Observation && f.Severity <> Severity.Informational)
        jobj
            [ "label", jstr c.Label
              "minute", jint c.Minute
              "at", jtime c.At
              "trigger", jstr c.Trigger
              "head", jopt jstr c.HeadSha
              "lastCommitAt", jopt jtime p.LastCommitAt
              "elapsedMinutes", jint p.ElapsedMinutes
              "remainingMinutes", jint p.RemainingMinutes
              "mandatory", jint p.MandatoryRequirements
              "codeCommitted", jint p.CodeCommittedRequirements
              "demonstrated", jint p.DemonstratedRequirements
              "verified", jint (p.VerifiedCatalog + p.VerifiedFrozenPlan)
              "verifiedCatalog", jint p.VerifiedCatalog
              "verifiedFrozenPlan", jint p.VerifiedFrozenPlan
              "frozenPlanMandatory", jint p.FrozenPlanMandatory
              "catalogMandatory", jint p.CatalogMandatory
              "blockers", c.Findings |> List.filter (fun f -> f.Rule = "OBS-005") |> List.map (fun f -> jstr f.Title) |> jarr
              "alerts",
              active
              |> List.sortByDescending (fun f -> severityRank f.Severity)
              |> List.map (fun f -> jobj [ "severity", jstr (severityCode f.Severity); "rule", jstr f.Rule; "title", jstr f.Title; "intervention", jbool f.Intervention ])
              |> jarr
              "newAlerts", jint c.Alerts.Length
              "recovery", jstr (readinessCode c.Recovery.Readiness)
              "recoveryMissing", jstrs c.Recovery.Missing
              "recommendedAction", jstr (recommended c.Findings) ]

    let run (argv: string list) =
        let started = Diagnostics.Stopwatch.StartNew()
        let cpu0 = Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
        let scopeText = File.ReadAllText(requiredArg "--scope" argv)
        let timelineText = File.ReadAllText(requiredArg "--timeline" argv)
        let outDir = requiredArg "--out" argv
        let scope = Decode.scope scopeText |> Result.defaultWith (fun e -> failwith $"scope: {e}")
        let timeline = Decode.timeline timelineText |> Result.defaultWith (fun e -> failwith $"timeline: {e}")
        let inputDigest = sha256Text (sha256Text scopeText + sha256Text timelineText)
        let modes = [ Visibility.Local; Visibility.Remote ]
        let results =
            modes
            |> List.map (fun mode ->
                let points = Replay.evaluationPoints mode (fixedMinutes @ [ extensionMinute ]) extensionMinute timeline
                mode, Engine.replay scope timeline mode points)
        let records =
            results
            |> List.collect (fun (mode, cs) ->
                cs
                |> List.mapi (fun i c ->
                    let node = checkpoint inputDigest c
                    let text = render node
                    let name = sprintf "%s/%03d-T%+04d.json" (visibilityCode mode) i c.Minute
                    writeText (Path.Combine(outDir, "checkpoints", name)) text
                    name, sha256Text text, c))
        writeJson
            (Path.Combine(outDir, "checkpoints", "INDEX.json"))
            (jobj
                [ "schema", jstr "specula.observer-checkpoint-index/1"
                  "inputDigest", jstr inputDigest
                  "scopeDigest", jstr scope.Digest
                  "description", jstr "Every checkpoint record with its sha256. Records are written once per replay from the snapshot of their instant and never edited; a changed record changes its digest."
                  "records", records |> List.map (fun (n, d, c) -> jobj [ "file", jstr n; "sha256", jstr d; "label", jstr c.Label; "mode", jstr (visibilityCode c.Mode); "trigger", jstr c.Trigger ]) |> jarr ])
        results
        |> List.iter (fun (mode, cs) ->
            writeJson
                (Path.Combine(outDir, $"alerts-{visibilityCode mode}.json"))
                (jobj
                    [ "schema", jstr "specula.observer-alerts/1"
                      "mode", jstr (visibilityCode mode)
                      "alerts", cs |> List.collect (fun c -> c.Alerts) |> List.map alert |> jarr ]))
        // Recovery handoff at the historical stopping point (T+115).
        let stopAt = Replay.atMinute timeline.Clock 115
        let handoffs =
            modes
            |> List.map (fun mode ->
                let snapshot = Replay.snapshotAt mode stopAt timeline
                let _, findings = Rules.evaluate scope snapshot
                let h = Handoff.generate scope snapshot findings
                let violations = Handoff.validate scope snapshot h
                writeJson (Path.Combine(outDir, $"handoff-{visibilityCode mode}.json")) (handoff h violations)
                mode, h, violations)
        let elapsed = started.ElapsedMilliseconds
        let cpu = (Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds
        let evaluations = results |> List.sumBy (fun (_, cs) -> cs.Length)
        writeJson
            (Path.Combine(outDir, "compute.json"))
            (jobj
                [ "schema", jstr "specula.observer-compute/1"
                  "replayWallMs", jint64 elapsed
                  "replayCpuMs", jint (int cpu)
                  "evaluations", jint evaluations
                  "modelCalls", jint 0
                  "note", jstr "The observer is deterministic: no language-model call is made during replay or collection. Wall and CPU time are for this process on the machine that ran it." ])
        let web =
            jobj
                [ "schema", jstr "specula.observer-replay-view/1"
                  "repository", jstr timeline.Repository
                  "t0", jtime timeline.Clock.T0
                  "deadline", jtime timeline.Clock.Deadline
                  "budgetMinutes", jint timeline.Clock.BudgetMinutes
                  "scopeDigest", jstr scope.Digest
                  "inputDigest", jstr inputDigest
                  "historical", jbool true
                  "modes",
                  results
                  // Fixed checkpoints and every evaluation that raised an alert.
                  |> List.map (fun (mode, cs) ->
                      let shown = cs |> List.filter (fun c -> c.Trigger.Contains "checkpoint T+" || not c.Alerts.IsEmpty)
                      jobj [ "mode", jstr (visibilityCode mode); "checkpoints", shown |> List.map viewCheckpoint |> jarr ])
                  |> jarr
                  "handoff",
                  handoffs
                  |> List.tryFind (fun (m, _, _) -> m = Visibility.Local)
                  |> Option.map (fun (_, h, v) ->
                      jobj
                          [ "at", jtime h.GeneratedAt
                            "currentCommit", jopt jstr h.CurrentCommit
                            "remainingMandatory", jint h.RemainingMandatory.Length
                            "nextActions", jstrs (h.NextActions |> List.truncate 4)
                            "consistent", jbool v.IsEmpty ])
                  |> Option.defaultValue jnull ]
        writeJson (arg "--web" argv |> Option.defaultValue (Path.Combine(outDir, "observer-view.json"))) web
        eprintfn "replay: %d evaluations, %d records, %d ms wall, %.0f ms CPU" evaluations records.Length elapsed cpu
