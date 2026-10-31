namespace Specula.Engine

open System

/// SHA-accurate verification (SPEC-UI-004, SPEC-G-001, SPEC-G-006, QG-001).
/// A status is only ever computed from records whose head SHA equals the
/// SHA being judged. A passing run for any other SHA is never evidence.
module Status =

    type HeadVerdict =
        | HeadPassing
        | HeadFailing
        | HeadRunning
        | HeadUnknown of reason: string

    let verdictLabel verdict =
        match verdict with
        | HeadPassing -> "Passing"
        | HeadFailing -> "Failing"
        | HeadRunning -> "Running"
        | HeadUnknown _ -> "Unknown"

    let outcomeLabel outcome =
        match outcome with
        | Queued -> "Queued"
        | Running -> "Running"
        | Passed -> "Passed"
        | Failed -> "Failed"
        | Cancelled -> "Cancelled"
        | Skipped -> "Skipped"
        | Neutral -> "Neutral"
        | UnknownOutcome -> "Unknown"

    /// Forma status-lozenge state: ok | attention | blocked | unknown.
    /// Success colour only for evidenced success.
    let outcomeState outcome =
        match outcome with
        | Passed -> "ok"
        | Failed -> "blocked"
        | Queued | Running | Cancelled -> "attention"
        | Skipped | Neutral | UnknownOutcome -> "unknown"

    let verdictState verdict =
        match verdict with
        | HeadPassing -> "ok"
        | HeadFailing -> "blocked"
        | HeadRunning -> "attention"
        | HeadUnknown _ -> "unknown"

    let private sameSha (a: string) (b: string) =
        String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

    /// Latest attempt per workflow (reruns supersede earlier attempts).
    let latestRunsPerWorkflow (runs: WorkflowRun list) : WorkflowRun list =
        runs
        |> List.groupBy (fun r -> r.Name)
        |> List.map (fun (_, group) -> group |> List.maxBy (fun r -> r.RunAttempt, r.CreatedAt |> Option.defaultValue DateTimeOffset.MinValue, r.Id))

    let runsForSha (sha: string) (runs: WorkflowRun list) =
        runs |> List.filter (fun r -> sameSha r.HeadSha sha) |> latestRunsPerWorkflow

    let checksForSha (sha: string) (checks: CheckRun list) =
        checks |> List.filter (fun c -> sameSha c.HeadSha sha)

    let private isFailure o = o = Failed
    let private isPending o = o = Queued || o = Running

    /// Aggregate outcomes that all belong to one SHA.
    let aggregate (outcomes: Outcome list) : HeadVerdict =
        match outcomes with
        | [] -> HeadUnknown "No check runs or workflow runs were found for this commit."
        | _ when outcomes |> List.exists isFailure -> HeadFailing
        | _ when outcomes |> List.exists isPending -> HeadRunning
        | _ when outcomes |> List.contains Passed && outcomes |> List.forall (fun o -> o = Passed || o = Skipped || o = Neutral) -> HeadPassing
        | _ when outcomes |> List.contains Cancelled -> HeadUnknown "A run for this commit was cancelled."
        | _ -> HeadUnknown "Runs for this commit reported no passing result."

    let headVerdict (sha: string) (runs: WorkflowRun list) (checks: CheckRun list) : HeadVerdict =
        let checkOutcomes = checksForSha sha checks |> List.map (fun c -> c.Outcome)
        let runOutcomes = runsForSha sha runs |> List.map (fun r -> r.Outcome)
        // Check runs are the authoritative per-job record when present; workflow
        // runs cover repositories whose checks endpoint returned nothing.
        aggregate (if List.isEmpty checkOutcomes then runOutcomes else checkOutcomes @ runOutcomes)

    /// The most recent passing run on a different SHA, used only to warn that
    /// it does NOT verify HEAD (acceptance scenario 8).
    let olderPassingRun (headSha: string) (runs: WorkflowRun list) : WorkflowRun option =
        runs
        |> List.filter (fun r -> r.Outcome = Passed && not (sameSha r.HeadSha headSha))
        |> List.sortByDescending (fun r -> r.CreatedAt)
        |> List.tryHead

    // ---- Deployment truth (SPEC-G-001) ----

    type TruthRow =
        { Id: string
          Label: string
          State: string
          Value: string
          Detail: string
          Url: string option }

    let private nameMatches (fragments: string list) (name: string) =
        fragments |> List.exists (fun f -> name.Contains(f, StringComparison.OrdinalIgnoreCase))

    let deploymentRunsForSha (sha: string) (runs: WorkflowRun list) =
        runsForSha sha runs |> List.filter (fun r -> nameMatches [ "deploy"; "pages"; "release"; "publish" ] r.Name)

    let latestStatus (statuses: DeploymentStatus list) =
        statuses |> List.sortByDescending (fun s -> s.CreatedAt, s.Id) |> List.tryHead

    let smokeChecksForSha (sha: string) (checks: CheckRun list) (runs: WorkflowRun list) =
        let fromChecks = checksForSha sha checks |> List.filter (fun c -> nameMatches [ "smoke" ] c.Name) |> List.map (fun c -> c.Name, c.Outcome, c.HtmlUrl)
        let fromRuns = runsForSha sha runs |> List.filter (fun r -> nameMatches [ "smoke" ] r.Name) |> List.map (fun r -> r.Name, r.Outcome, r.HtmlUrl)
        fromChecks @ fromRuns

    let truthRows
        (headSha: string option)
        (verdict: HeadVerdict)
        (runs: WorkflowRun list)
        (checks: CheckRun list)
        (deployments: Deployment list)
        (statuses: Map<int64, DeploymentStatus list>)
        : TruthRow list =
        match headSha with
        | None ->
            [ { Id = "ci"; Label = "Build / CI"; State = "unknown"; Value = "Unknown"; Detail = "HEAD commit not observed."; Url = None } ]
        | Some sha ->
            let ciRow =
                { Id = "ci"
                  Label = "Build / CI for HEAD"
                  State = verdictState verdict
                  Value = verdictLabel verdict
                  Detail =
                    match verdict with
                    | HeadUnknown reason -> reason
                    | _ -> $"Aggregated from check runs and workflow runs whose head SHA is {Repo.shortSha sha}."
                  Url = None }

            let deployRuns = deploymentRunsForSha sha runs
            let workflowRow =
                match deployRuns with
                | [] ->
                    { Id = "deploy-workflow"; Label = "Deployment workflow"; State = "unknown"; Value = "Not found"
                      Detail = "No workflow run for HEAD with deploy, pages, release or publish in its name."; Url = None }
                | runs ->
                    let v = aggregate (runs |> List.map (fun r -> r.Outcome))
                    let first = runs |> List.head
                    { Id = "deploy-workflow"; Label = "Deployment workflow"; State = verdictState v; Value = verdictLabel v
                      Detail = $"Matched by workflow name: {first.Name}. A green workflow does not prove the application works."
                      Url = Some first.HtmlUrl }

            let headDeployments = deployments |> List.filter (fun d -> String.Equals(d.Sha, sha, StringComparison.OrdinalIgnoreCase))
            let recorded =
                headDeployments
                |> List.choose (fun d -> statuses |> Map.tryFind d.Id |> Option.bind latestStatus |> Option.map (fun s -> d, s))
            let successful = recorded |> List.filter (fun (_, s) -> s.State = DeploySuccess)
            let urlRow =
                match successful |> List.tryPick (fun (d, s) -> s.EnvironmentUrl |> Option.bind Repo.safeHttpsUrl |> Option.map (fun u -> d, u)) with
                | Some(d, url) ->
                    { Id = "deploy-url"; Label = "Deployment URL available"; State = "attention"; Value = "Recorded"
                      Detail = $"GitHub deployment {d.Id} ({d.Environment}) reports {url}. Specula has not opened it."; Url = Some url }
                | None when List.isEmpty headDeployments ->
                    { Id = "deploy-url"; Label = "Deployment URL available"; State = "unknown"; Value = "Not found"
                      Detail = "No GitHub deployment record exists for HEAD."; Url = None }
                | None ->
                    { Id = "deploy-url"; Label = "Deployment URL available"; State = "unknown"; Value = "Not recorded"
                      Detail = "A deployment record exists for HEAD but no successful status with an environment URL was observed."; Url = None }

            let smoke = smokeChecksForSha sha checks runs
            let smokeRow =
                match smoke with
                | [] ->
                    { Id = "smoke"; Label = "Application smoke test"; State = "unknown"; Value = "Not verified"
                      Detail = "No independent smoke-test evidence for HEAD. A passing build or deployment does not mean the application was opened and tested."
                      Url = None }
                | items ->
                    let v = aggregate (items |> List.map (fun (_, o, _) -> o))
                    let name, _, url = items |> List.head
                    { Id = "smoke"; Label = "Application smoke test"
                      State = verdictState v
                      Value = (match v with HeadPassing -> "Reported passing" | other -> verdictLabel other)
                      Detail = $"Reported by the observed repository's check \"{name}\" on HEAD (matched by name). Specula did not run it."
                      Url = Some url }

            [ ciRow; workflowRow; urlRow; smokeRow ]
