namespace Specula.Engine

open System

/// Event normalization, deduplication and filtering (SPEC-D-003, SPEC-D-004,
/// CAP-008). Stable IDs are derived from GitHub record identity, so the same
/// record observed across refreshes collapses to one event.
module Events =

    let ofCommit (c: Commit) : SourceEvent option =
        c.Date
        |> Option.map (fun date ->
            { StableId = $"commit:{c.Sha}"
              Kind = CommitEvent
              Title = GitHub.firstLine c.Message
              Summary = "Commit"
              SourceUrl = c.HtmlUrl
              EventTime = date
              Sha = Some c.Sha
              Actor = c.AuthorLogin |> Option.orElse c.AuthorName
              Outcome = None })

    /// One PR can contribute opened, merged and closed events.
    let ofPull (p: PullRequest) : SourceEvent list =
        let make suffix verb (time: DateTimeOffset option) sha =
            time
            |> Option.map (fun t ->
                { StableId = $"pr:{p.Number}:{suffix}"
                  Kind = PullRequestEvent
                  Title = $"#{p.Number} {p.Title}"
                  Summary = verb
                  SourceUrl = p.HtmlUrl
                  EventTime = t
                  Sha = sha
                  Actor = p.Author
                  Outcome = None })
        [ make "opened" "Pull request opened" p.CreatedAt p.HeadSha
          (match p.State with
           | PullMerged -> make "merged" "Pull request merged" p.MergedAt p.MergeSha
           | PullClosed -> make "closed" "Pull request closed without merge" p.ClosedAt p.HeadSha
           | PullOpen -> None) ]
        |> List.choose id

    let ofRun (r: WorkflowRun) : SourceEvent option =
        r.CreatedAt
        |> Option.map (fun t ->
            { StableId = $"run:{r.Id}:{r.RunAttempt}"
              Kind = WorkflowEvent
              Title = r.Name
              Summary =
                let attempt = if r.RunAttempt > 1L then $" (attempt {r.RunAttempt})" else ""
                $"Workflow run{attempt}" + (r.Event |> Option.map (fun e -> $" · {e}") |> Option.defaultValue "")
              SourceUrl = r.HtmlUrl
              EventTime = t
              Sha = Some r.HeadSha
              Actor = None
              Outcome = Some r.Outcome })

    let ofCheck (c: CheckRun) : SourceEvent option =
        c.StartedAt
        |> Option.orElse c.CompletedAt
        |> Option.map (fun t ->
            { StableId = $"check:{c.Id}"
              Kind = CheckEvent
              Title = c.Name
              Summary = "Check run" + (c.App |> Option.map (fun a -> $" · {a}") |> Option.defaultValue "")
              SourceUrl = c.HtmlUrl
              EventTime = t
              Sha = Some c.HeadSha
              Actor = None
              Outcome = Some c.Outcome })

    let deploymentStateLabel state =
        match state with
        | DeploySuccess -> "success"
        | DeployFailure -> "failure"
        | DeployError -> "error"
        | DeployInactive -> "inactive"
        | DeployInProgress -> "in progress"
        | DeployQueued -> "queued"
        | DeployPending -> "pending"
        | DeployUnknown other -> $"\"{other}\""

    let ofDeployment (repo: RepoId) (statuses: Map<int64, DeploymentStatus list>) (d: Deployment) : SourceEvent option =
        let latest = statuses |> Map.tryFind d.Id |> Option.bind Status.latestStatus
        d.CreatedAt
        |> Option.map (fun t ->
            { StableId = $"deployment:{d.Id}"
              Kind = DeploymentEvent
              Title = $"Deployment to {d.Environment}"
              Summary =
                match latest with
                | Some s -> $"Deployment · latest status {deploymentStateLabel s.State}"
                | None -> "Deployment · status not observed"
              SourceUrl = $"{Repo.repoUrl repo}/deployments"
              EventTime = t
              Sha = Some d.Sha
              Actor = d.Creator
              Outcome =
                latest
                |> Option.map (fun s ->
                    match s.State with
                    | DeploySuccess -> Passed
                    | DeployFailure | DeployError -> Failed
                    | DeployInProgress -> Running
                    | DeployQueued | DeployPending -> Queued
                    | DeployInactive -> Neutral
                    | DeployUnknown _ -> UnknownOutcome) })

    /// Deduplicate by StableId, keeping the most recently observed version,
    /// then order newest first with a deterministic tie-break.
    let normalize (events: SourceEvent list) : SourceEvent list =
        events
        |> List.rev
        |> List.distinctBy (fun e -> e.StableId)
        |> List.sortWith (fun a b ->
            match compare b.EventTime a.EventTime with
            | 0 -> compare a.StableId b.StableId
            | c -> c)

    let dedupeBy (key: 'a -> 'k) (items: 'a list) : 'a list =
        items |> List.distinctBy key

    /// Merge newly fetched records into existing ones: new versions replace
    /// old ones with the same key; order follows the newest list first.
    let merge (key: 'a -> 'k) (existing: 'a list) (incoming: 'a list) : 'a list =
        incoming @ existing |> List.distinctBy key

    type Filter =
        | AllEvents
        | OnlyCommits
        | OnlyPulls
        | OnlyChecks
        | OnlyDeployments

    let filterKey filter =
        match filter with
        | AllEvents -> "all"
        | OnlyCommits -> "commits"
        | OnlyPulls -> "pulls"
        | OnlyChecks -> "checks"
        | OnlyDeployments -> "deployments"

    let filterOfKey key =
        match key with
        | "commits" -> Some OnlyCommits
        | "pulls" -> Some OnlyPulls
        | "checks" -> Some OnlyChecks
        | "deployments" -> Some OnlyDeployments
        | "all" -> Some AllEvents
        | _ -> None

    let matches filter (e: SourceEvent) =
        match filter, e.Kind with
        | AllEvents, _ -> true
        | OnlyCommits, CommitEvent -> true
        | OnlyPulls, PullRequestEvent -> true
        | OnlyChecks, (WorkflowEvent | CheckEvent) -> true
        | OnlyDeployments, DeploymentEvent -> true
        | _ -> false

    let inWindow (start: DateTimeOffset) (finish: DateTimeOffset) (e: SourceEvent) =
        e.EventTime >= start && e.EventTime <= finish

    let kindLabel kind =
        match kind with
        | CommitEvent -> "Commit"
        | PullRequestEvent -> "Pull request"
        | WorkflowEvent -> "Workflow run"
        | CheckEvent -> "Check run"
        | DeploymentEvent -> "Deployment"

    /// Text glyph shown with the label (icon + text, never colour alone).
    let kindGlyph kind =
        match kind with
        | CommitEvent -> "●"
        | PullRequestEvent -> "⇄"
        | WorkflowEvent -> "▶"
        | CheckEvent -> "✓"
        | DeploymentEvent -> "⇪"
