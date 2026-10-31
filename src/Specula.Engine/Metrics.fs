namespace Specula.Engine

open System
open Specula.Engine.Preferences

/// Efficiency metrics and milestones (SPEC-UI-022, SPEC-D-011, SPEC-D-012,
/// SPEC-G-002, SPEC-G-010). Every metric declares its definition,
/// denominator, window and provenance. Insufficient evidence yields
/// NotMeasurable — never a zero or an estimate. Commit counts are never a
/// productivity measure.
module Metrics =

    let definitionVersion = "specula.metrics/1"
    let notMeasurable = "Not measurable from available evidence"

    type Metric =
        { Id: string
          Name: string
          Value: string option
          Reason: string
          Definition: string
          Denominator: string
          Window: string
          Provenance: string }

    type Inputs =
        { Kickoff: DateTimeOffset option
          Runs: WorkflowRun list
          RunsTotal: int64 option
          Deployments: Deployment list
          DeploymentStatuses: Map<int64, DeploymentStatus list>
          Requirements: Evidence.Requirement list option
          Corrections: Evidence.Correction list option }

    let private completedAt (r: WorkflowRun) = r.UpdatedAt |> Option.orElse r.CreatedAt

    let private isCompleted (r: WorkflowRun) =
        match r.Outcome with
        | Passed | Failed | Cancelled | Skipped | Neutral -> true
        | _ -> false

    let private runsWindow (runs: WorkflowRun list) =
        let times = runs |> List.choose (fun r -> r.CreatedAt)
        match times with
        | [] -> "No workflow runs loaded"
        | _ -> $"{List.length runs} loaded runs, {Time.utc (List.min times)} to {Time.utc (List.max times)}"

    /// True when loaded runs may not reach back to `since`.
    let private truncatedBefore (since: DateTimeOffset) (inputs: Inputs) =
        let loaded = int64 (List.length inputs.Runs)
        let oldest = inputs.Runs |> List.choose (fun r -> r.CreatedAt) |> List.sort |> List.tryHead
        match inputs.RunsTotal, oldest with
        | Some total, Some o when total > loaded && o > since -> true
        | _ -> false

    let timeToFirstPassingCi (inputs: Inputs) : Metric =
        let shell value reason =
            { Id = "time-to-first-passing-ci"
              Name = "Time to first passing CI"
              Value = value
              Reason = reason
              Definition = "Configured kickoff to the completion of the earliest workflow run on the observed branch that passed and started at or after kickoff."
              Denominator = "One interval"
              Window = runsWindow inputs.Runs
              Provenance = "GitHub Actions workflow runs (created_at, updated_at, conclusion)" }
        match inputs.Kickoff with
        | None -> shell None "No kickoff is configured. Set one in Settings; Specula never infers kickoff from commit times."
        | Some kickoff when truncatedBefore kickoff inputs -> shell None "The loaded workflow runs do not reach back to kickoff."
        | Some kickoff ->
            inputs.Runs
            |> List.filter (fun r -> r.Outcome = Passed && r.CreatedAt |> Option.exists (fun c -> c >= kickoff))
            |> List.choose (fun r -> completedAt r |> Option.map (fun t -> t, r))
            |> List.sortBy fst
            |> List.tryHead
            |> function
                | Some(t, r) -> shell (Some(Time.duration (t - kickoff))) $"Run \"{r.Name}\" #{r.Id} completed {Time.utc t}."
                | None -> shell None "No passing workflow run after kickoff was observed."

    let timeToFirstDeployment (inputs: Inputs) : Metric =
        let shell value reason =
            { Id = "time-to-first-deployment"
              Name = "Time to first evidenced deployment"
              Value = value
              Reason = reason
              Definition = "Configured kickoff to the earliest GitHub deployment status of \"success\" created at or after kickoff."
              Denominator = "One interval"
              Window = $"{List.length inputs.Deployments} loaded deployments; statuses read for {Map.count inputs.DeploymentStatuses}"
              Provenance = "GitHub Deployments API (deployment statuses)" }
        match inputs.Kickoff with
        | None -> shell None "No kickoff is configured."
        | Some kickoff ->
            inputs.DeploymentStatuses
            |> Map.toList
            |> List.collect snd
            |> List.filter (fun s -> s.State = DeploySuccess)
            |> List.choose (fun s -> s.CreatedAt)
            |> List.filter (fun t -> t >= kickoff)
            |> List.sort
            |> List.tryHead
            |> function
                | Some t -> shell (Some(Time.duration (t - kickoff))) $"Successful deployment status recorded {Time.utc t}. A recorded deployment is not a smoke test."
                | None when List.isEmpty inputs.Deployments -> shell None "The repository exposes no GitHub deployment records."
                | None -> shell None "No successful deployment status after kickoff was observed in the statuses read."

    /// Failure followed by the next passing run of the same workflow.
    let recoveries (runs: WorkflowRun list) : (WorkflowRun * WorkflowRun * TimeSpan) list =
        runs
        |> List.filter isCompleted
        |> List.groupBy (fun r -> r.Name)
        |> List.collect (fun (_, group) ->
            let ordered = group |> List.sortBy (fun r -> completedAt r, r.Id)
            let folder (pendingFailure: WorkflowRun option, acc) (run: WorkflowRun) =
                match pendingFailure, run.Outcome with
                | None, Failed -> Some run, acc
                | Some f, Passed ->
                    let span =
                        match completedAt f, completedAt run with
                        | Some a, Some b -> b - a
                        | _ -> TimeSpan.Zero
                    None, (f, run, span) :: acc
                | pending, _ -> pending, acc
            ordered |> List.fold folder (None, []) |> snd |> List.rev)

    let failureToRecovery (inputs: Inputs) : Metric =
        let completed = inputs.Runs |> List.filter isCompleted
        let failures = completed |> List.filter (fun r -> r.Outcome = Failed) |> List.length
        let cycles = recoveries inputs.Runs
        let value, reason =
            match completed, cycles with
            | [], _ -> None, "No completed workflow runs were loaded."
            | _, [] when failures = 0 -> Some "No failures observed", $"0 failed of {List.length completed} completed runs."
            | _, [] -> None, $"{failures} failed runs, none followed by a passing run of the same workflow yet."
            | _, cycles ->
                let spans = cycles |> List.map (fun (_, _, s) -> s)
                let med = Time.median spans |> Option.map Time.duration |> Option.defaultValue "?"
                let longest = spans |> List.max |> Time.duration
                Some $"Median {med}", $"{List.length cycles} recoveries; longest {longest}."
        { Id = "failure-to-recovery"
          Name = "Failure-to-recovery time"
          Value = value
          Reason = reason
          Definition = "For each workflow, the time from a failed run's completion to the completion of the next passing run of the same workflow."
          Denominator = $"{List.length cycles} recovery intervals across {List.length completed} completed runs"
          Window = runsWindow inputs.Runs
          Provenance = "GitHub Actions workflow runs on the observed branch" }

    let verifiedRequirementsPerHour (inputs: Inputs) : Metric =
        let verified = inputs.Requirements |> Option.map (List.filter (fun r -> r.State = Verified) >> List.length)
        { Id = "verified-requirements-per-hour"
          Name = "Verified requirements per hour"
          Value = None
          Reason =
            match verified with
            | None -> "No supported requirements evidence was found."
            | Some n -> $"Supported evidence lists {n} verified requirements but records no verification timestamps, so a rate cannot be computed."
          Definition = "Requirements whose supported evidence changed to Verified within the window, divided by elapsed hours since kickoff."
          Denominator = "Elapsed hours since configured kickoff"
          Window = "Requires per-requirement verification timestamps"
          Provenance = "Supported requirement evidence (causa.traceability/1)" }

    let correctionCycles (inputs: Inputs) : Metric =
        let cycles = recoveries inputs.Runs |> List.length
        let recorded = inputs.Corrections |> Option.map List.length
        let value =
            match inputs.Runs, recorded with
            | [], None -> None
            | _ ->
                let r = recorded |> Option.map (fun n -> $"; {n} recorded corrections") |> Option.defaultValue ""
                Some $"{cycles} CI recoveries{r}"
        { Id = "correction-cycles"
          Name = "Correction cycles"
          Value = value
          Reason =
            match recorded with
            | Some _ -> "CI recoveries are observed; recorded corrections come from evidence/defects.md."
            | None -> "CI recoveries are observed; no supported correction record was found."
          Definition = "Count of failed-then-recovered workflow sequences, reported separately from corrections recorded in supported evidence."
          Denominator = "Loaded workflow runs"
          Window = runsWindow inputs.Runs
          Provenance = "GitHub Actions workflow runs; evidence/defects.md when present" }

    let all (inputs: Inputs) : Metric list =
        [ timeToFirstPassingCi inputs
          timeToFirstDeployment inputs
          failureToRecovery inputs
          verifiedRequirementsPerHour inputs
          correctionCycles inputs ]

    // ---- Milestones (planned vs observed) ----

    type Observation =
        | Observed of at: DateTimeOffset * source: string * url: string option
        | NotObserved of reason: string

    type Milestone =
        { Kind: MilestoneKind
          Planned: DateTimeOffset option
          Observation: Observation }

    type MilestoneInputs =
        { Repository: Repository option
          HeadDate: DateTimeOffset option
          Kickoff: DateTimeOffset option
          Runs: WorkflowRun list
          DeploymentStatuses: Map<int64, DeploymentStatus list>
          Deployments: Deployment list
          RequirementsFound: bool
          EvidenceUrl: string option }

    let private afterKickoff (kickoff: DateTimeOffset option) (t: DateTimeOffset) =
        kickoff |> Option.forall (fun k -> t >= k)

    let observe (inputs: MilestoneInputs) (kind: MilestoneKind) : Observation =
        let needKickoff f =
            match inputs.Kickoff with
            | None -> NotObserved "Configure a kickoff time to determine \"first\" milestones."
            | Some _ -> f ()
        match kind with
        | RepositoryCreated ->
            match inputs.Repository |> Option.bind (fun r -> r.CreatedAt |> Option.map (fun c -> c, r.HtmlUrl)) with
            | Some(c, url) -> Observed(c, "Repository metadata (created_at)", Some url)
            | None -> NotObserved "Repository metadata not loaded."
        | FirstCiRun ->
            needKickoff (fun () ->
                inputs.Runs
                |> List.filter (fun r -> r.CreatedAt |> Option.exists (afterKickoff inputs.Kickoff))
                |> List.sortBy (fun r -> r.CreatedAt)
                |> List.tryHead
                |> function
                    | Some r -> Observed(r.CreatedAt.Value, $"Workflow run \"{r.Name}\" started", Some r.HtmlUrl)
                    | None -> NotObserved "No workflow run after kickoff in the loaded runs.")
        | FirstPassingCi ->
            needKickoff (fun () ->
                inputs.Runs
                |> List.filter (fun r -> r.Outcome = Passed && r.CreatedAt |> Option.exists (afterKickoff inputs.Kickoff))
                |> List.choose (fun r -> completedAt r |> Option.map (fun t -> t, r))
                |> List.sortBy fst
                |> List.tryHead
                |> function
                    | Some(t, r) -> Observed(t, $"Workflow run \"{r.Name}\" passed", Some r.HtmlUrl)
                    | None -> NotObserved "No passing workflow run after kickoff in the loaded runs.")
        | FirstDeployment ->
            needKickoff (fun () ->
                inputs.DeploymentStatuses
                |> Map.toList
                |> List.collect (fun (id, statuses) -> statuses |> List.map (fun s -> id, s))
                |> List.filter (fun (_, s) -> s.State = DeploySuccess && s.CreatedAt |> Option.exists (afterKickoff inputs.Kickoff))
                |> List.sortBy (fun (_, s) -> s.CreatedAt)
                |> List.tryHead
                |> function
                    | Some(id, s) ->
                        let env = inputs.Deployments |> List.tryFind (fun d -> d.Id = id) |> Option.map (fun d -> d.Environment) |> Option.defaultValue "unknown"
                        Observed(s.CreatedAt.Value, $"Deployment {id} to {env} reported success", None)
                    | None -> NotObserved "No successful deployment status after kickoff was observed.")
        | RequirementsEvidence ->
            match inputs.RequirementsFound, inputs.HeadDate with
            | true, Some d -> Observed(d, "Supported requirements evidence present at HEAD (first appearance not determined)", inputs.EvidenceUrl)
            | true, None -> NotObserved "Requirements evidence found but the HEAD commit time is unknown."
            | false, _ -> NotObserved "No supported requirements evidence at HEAD."
        | AcceptanceVerdict -> NotObserved "No independent acceptance verdict exists in supported evidence."

    let milestones (competition: Competition) (inputs: MilestoneInputs) : Milestone list =
        let planned kind =
            match competition.Kickoff with
            | None -> None
            | Some k ->
                competition.Checkpoints
                |> List.tryFind (fun c -> c.Milestone = kind)
                |> Option.map (fun c -> k.AddMinutes(float c.OffsetMinutes))
        milestoneKinds
        |> List.map (fun kind -> { Kind = kind; Planned = planned kind; Observation = observe inputs kind })

    type Clock =
        { Kickoff: DateTimeOffset
          Deadline: DateTimeOffset
          Elapsed: TimeSpan
          Remaining: TimeSpan
          Passed: bool }

    /// SPEC-G-005: elapsed and remaining from the device clock.
    let clock (now: DateTimeOffset) (competition: Competition) : Clock option =
        competition.Kickoff
        |> Option.map (fun k ->
            let deadline = k.AddMinutes(float competition.DurationMinutes)
            { Kickoff = k
              Deadline = deadline
              Elapsed = if now > k then now - k else TimeSpan.Zero
              Remaining = if deadline > now then deadline - now else TimeSpan.Zero
              Passed = now >= deadline })
