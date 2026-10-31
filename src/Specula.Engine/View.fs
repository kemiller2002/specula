namespace Specula.Engine

open System
open Limen.Contract.Core
open Specula.Engine.Model
open Specula.Engine.Preferences

/// Projection: Model -> Limen ViewState. Total and pure: every key bound by
/// index.html is present in every projection, and every list item carries
/// every field its template binds.
module View =

    type Entry = string * ViewValue
    type Item = Map<string, ViewPrimitive>

    let private t (k: string) (v: string) : Entry = k, ViewValue.Text v
    let private f (k: string) (v: bool) : Entry = k, ViewValue.Flag v
    let private items (k: string) (v: Item list) : Entry = k, ViewValue.Items v
    let private pt (k: string) (v: string) = k, ViewPrimitive.Text v
    let private pf (k: string) (v: bool) = k, ViewPrimitive.Flag v
    let private item (fields: (string * ViewPrimitive) list) : Item = Map.ofList fields
    let private aria b = if b then "true" else "false"
    let private orDash (s: string) = if String.IsNullOrWhiteSpace s then "—" else s

    let dependencies =
        [ "limen-npm", "Limen browser kernel (npm @echelon-foundry/limen)", "0.9.1", "https://www.npmjs.com/package/@echelon-foundry/limen/v/0.9.1"
          "limen-fsharp", "Limen F# contract (EchelonFoundry.Limen.Contract and .Guest)", "0.9.1", "https://github.com/kemiller2002/limen/releases/tag/v0.9.1"
          "forma", "Forma design system (npm @echelon-foundry/design-system)", "0.13.0", "https://www.npmjs.com/package/@echelon-foundry/design-system/v/0.13.0"
          "dotnet", ".NET WebAssembly runtime", "net10.0", "https://dotnet.microsoft.com/" ]

    // ---- shared pieces ----

    let fetchSummary (model: Model) =
        match model.Fetch with
        | Idle -> "unknown", "Not loaded", "No data has been requested yet."
        | Loading ->
            "attention",
            (if model.LastSuccess.IsSome then "Refreshing…" else "Connecting…"),
            (if model.LastSuccess.IsSome then "Showing the previous refresh until current GitHub data arrives." else "Requesting current data from GitHub.")
        | Fresh at -> "unknown", "Fresh", $"All sources read at {Time.local model.Zone at}."
        | Partial(at, reason) -> "attention", "Partial", $"Read at {Time.local model.Zone at}. {reason}"
        | Stale(_, reason) -> "attention", "Stale", reason + " Displayed data is from the last successful refresh and is not current."
        | RateLimited retry ->
            let until = retry |> Option.map (Time.local model.Zone) |> Option.defaultValue "an unknown time"
            "blocked", "Rate limited", $"GitHub's unauthenticated API limit is exhausted until {until}. Displayed data is not current."
        | FetchError reason -> "blocked", "Error", reason

    let private observedText (model: Model) (snapshot: Snapshot option) =
        snapshot |> Option.bind (fun s -> s.ObservedAt) |> Option.map (Time.local model.Zone) |> Option.defaultValue "in progress"

    let private eventItem (model: Model) (observed: string) (e: SourceEvent) : Item =
        let url = Repo.safeGitHubUrl e.SourceUrl
        let expanded = model.Expanded = Some e.StableId
        let kind = Events.kindLabel e.Kind
        item
            [ pt "id" e.StableId
              pt "kind" kind
              pt "glyph" (Events.kindGlyph e.Kind)
              pt "title" (orDash e.Title)
              pt "summary" e.Summary
              pt "time" (Time.local model.Zone e.EventTime)
              pt "timeIso" (Time.iso e.EventTime)
              pt "utc" (Time.utc e.EventTime)
              pt "sha" (e.Sha |> Option.map Repo.shortSha |> Option.defaultValue "")
              pt "shaFull" (e.Sha |> Option.defaultValue "")
              pf "hasSha" e.Sha.IsSome
              pt "url" (url |> Option.defaultValue "")
              pf "hasUrl" url.IsSome
              pt "linkLabel" $"View {kind.ToLowerInvariant()} on GitHub: {e.Title}"
              pt "outcome" (e.Outcome |> Option.map Status.outcomeLabel |> Option.defaultValue "")
              pt "outcomeState" (e.Outcome |> Option.map Status.outcomeState |> Option.defaultValue "unknown")
              pf "hasOutcome" e.Outcome.IsSome
              pt "actor" (e.Actor |> Option.map (fun a -> $"Attributed by GitHub to {a}") |> Option.defaultValue "No GitHub user attribution")
              pf "expanded" expanded
              pt "expandedAttr" (aria expanded)
              pt "toggleLabel" (if expanded then "Hide details" else "Details")
              pt "observed" observed ]

    // ---- shell ----

    let private navItems (model: Model) : Entry list =
        let current s = if model.Screen = s then "page" else "false"
        [ t "navOverview" (current Overview)
          t "navActivity" (current Activity)
          t "navVerification" (current Verification)
          t "navReplay" (current Replay)
          t "navSettings" (current Settings)
          t "navAbout" (current About) ]

    let private visibility (model: Model) : Entry list =
        let hide s = not model.Started || model.Screen <> s
        [ f "hidePicker" (hide Picker)
          f "hideOverview" (hide Overview)
          f "hideActivity" (hide Activity)
          f "hideVerification" (hide Verification)
          f "hideReplay" (hide Replay)
          f "hideSettings" (hide Settings)
          f "hideAbout" (hide About)
          f "starting" (not model.Started)
          f "bootDone" model.Started ]

    let private shell (model: Model) : Entry list =
        let state, label, detail = fetchSummary model
        let repo = model.Selection |> Option.map RepoId.text
        let rate =
            match model.RateLimit with
            | Some { Remaining = Some r; Limit = Some l; Reset = reset } ->
                let resetText = reset |> Option.map (fun x -> $", resets {Time.local model.Zone x}") |> Option.defaultValue ""
                $"GitHub API budget: {r} of {l} requests remaining{resetText}."
            | _ -> "GitHub API budget: not yet reported."
        [ t "theme" (Preferences.themeKey model.Prefs.Theme)
          t "documentTitle" ((repo |> Option.map (fun r -> $"{Model.screenTitle model.Screen} · {r} · Specula") |> Option.defaultValue "Specula"))
          t "screenTitle" (Model.screenTitle model.Screen)
          f "hasSelection" repo.IsSome
          f "noSelection" repo.IsNone
          t "repoText" (repo |> Option.defaultValue "No repository selected")
          t "repoLabel" (repo |> Option.map (fun r -> $"Observing {r}") |> Option.defaultValue "No repository selected")
          t "repoUrl" (model.Selection |> Option.map Repo.repoUrl |> Option.defaultValue "https://github.com/")
          f "refreshDisabled" (repo.IsNone || isLoading model)
          t "refreshLabel" (if isLoading model then "Refreshing…" else "Refresh")
          t "refreshBusy" (aria (isLoading model))
          t "fetchState" state
          t "fetchLabel" label
          t "fetchDetail" detail
          t "lastSuccess" (model.LastSuccess |> Option.map (fun x -> $"{Time.local model.Zone x} ({Time.ago model.Now x})") |> Option.defaultValue "never in this session")
          t "lastAttempt" (model.LastAttempt |> Option.map (Time.local model.Zone) |> Option.defaultValue "none")
          t "rateText" rate
          t "zoneLabel" (Time.offsetLabel (model.Zone.GetUtcOffset model.Now))
          f "hasNotice" model.Notice.IsSome
          t "notice" (model.Notice |> Option.defaultValue "")
          f "hasStorageNotice" model.StorageNotice.IsSome
          t "storageNotice" (model.StorageNotice |> Option.defaultValue "")
          f "hasShared" model.SharedLink.IsSome
          t "sharedText" (model.SharedLink |> Option.map (fun r -> $"You are viewing {RepoId.text r} from a shared link. Your saved repository is unchanged.") |> Option.defaultValue "")
          t "sourceSha" BuildInfo.SourceSha
          t "sourceShaShort" (Repo.shortSha BuildInfo.SourceSha) ]
        @ navItems model
        @ visibility model

    // ---- picker ----

    let private picker (model: Model) : Entry list =
        let recent =
            model.Prefs.RecentRepositories
            |> List.map (fun r ->
                item
                    [ pt "key" (RepoId.key r)
                      pt "label" (RepoId.text r)
                      pt "aria" $"Observe {RepoId.text r}"
                      pf "current" (model.Selection |> Option.exists (RepoId.same r)) ])
        let busy = model.Candidate.IsSome
        [ t "draft" model.Draft
          f "hasDraftError" model.DraftError.IsSome
          t "draftError" (model.DraftError |> Option.defaultValue "")
          t "draftInvalid" (aria model.DraftError.IsSome)
          f "pickerBusy" busy
          f "observeDisabled" false
          t "observeLabel" (if busy then "Checking repository…" else "Observe repository")
          t "pickerStatus" (model.Candidate |> Option.map (fun c -> $"Checking {RepoId.text c} on GitHub…") |> Option.defaultValue "")
          items "recent" recent
          f "hasRecent" (not recent.IsEmpty)
          f "noRecent" recent.IsEmpty ]

    // ---- verification helpers ----

    /// CI sources whose last read failed (rate limited, offline, error).
    let unreadableCi (s: Snapshot) : string list =
        [ "HEAD check runs", "check runs"; "workflow runs", "workflow runs" ]
        |> List.filter (fun (key, _) ->
            match s.Endpoints |> Map.tryFind key with
            | Some(EndpointFailed _) | Some EndpointRateLimited -> true
            | _ -> false)
        |> List.map snd

    let private headInfo (model: Model) (snapshot: Snapshot option) =
        let sha = snapshot |> Option.bind (fun s -> s.HeadSha)
        let commit = snapshot |> Option.bind (fun s -> sha |> Option.bind (fun h -> s.Commits |> List.tryFind (fun c -> c.Sha = h)))
        let verdict =
            match snapshot, sha with
            | Some s, Some h -> Status.headVerdict h s.Runs s.HeadChecks |> Status.qualify (unreadableCi s)
            | Some s, None when s.Endpoints |> Map.tryFind "commits" |> Option.exists (function EndpointEmpty _ -> true | _ -> false) ->
                Status.HeadUnknown "The repository has no commits on its default branch."
            | _ -> Status.HeadUnknown "The HEAD commit has not been observed yet."
        sha, commit, verdict

    let private headEntries (model: Model) (snapshot: Snapshot option) : Entry list =
        let sha, commit, verdict = headInfo model snapshot
        let branch = snapshot |> Option.bind (fun s -> s.Repository) |> Option.map (fun r -> r.DefaultBranch) |> Option.defaultValue "—"
        let older = match snapshot, sha with Some s, Some h -> Status.olderPassingRun h s.Runs | _ -> None
        [ t "branch" branch
          f "hasHead" sha.IsSome
          t "headSha" (sha |> Option.map Repo.shortSha |> Option.defaultValue "not observed")
          t "headShaFull" (sha |> Option.defaultValue "")
          t "headUrl" (commit |> Option.map (fun c -> c.HtmlUrl) |> Option.defaultValue "https://github.com/")
          t "headMessage" (commit |> Option.map (fun c -> GitHub.firstLine c.Message) |> Option.defaultValue "")
          t "headDate" (commit |> Option.bind (fun c -> c.Date) |> Option.map (Time.local model.Zone) |> Option.defaultValue "")
          t "headState" (Status.verdictState verdict)
          t "headLabel" (Status.verdictLabel verdict)
          t "headDetail"
              (match verdict, sha with
               | Status.HeadUnknown reason, _ -> reason
               | _, Some h -> $"Checked commit {Repo.shortSha h} only; runs for other commits are excluded."
               | _ -> "")
          t "headObserved" (observedText model snapshot)
          f "hasOlderPass" ((match verdict with Status.HeadPassing -> false | _ -> true) && older.IsSome)
          t "olderPass"
              (older
               |> Option.map (fun r -> $"A passing run (\"{r.Name}\") exists for older commit {Repo.shortSha r.HeadSha}. It does not verify HEAD.")
               |> Option.defaultValue "")
          t "olderPassUrl" (older |> Option.map (fun r -> r.HtmlUrl) |> Option.defaultValue "https://github.com/") ]

    let private coverageEntries (snapshot: Snapshot option) : Entry list =
        let sources = snapshot |> Option.map (fun s -> s.Evidence) |> Option.defaultValue []
        let c = Evidence.coverage sources
        [ t "coverageState" (Evidence.coverageLozenge c)
          t "coverageLabel" (Evidence.coverageLabel c)
          t "coverageDetail"
              (match c with
               | Evidence.CoverageSupported -> "Supported structured evidence was read at HEAD. Coverage is not verification."
               | Evidence.CoveragePartial -> "Some supported evidence was read; other allowlisted sources are missing, unsupported or unreadable."
               | Evidence.CoverageNotFound -> "No supported Echelon evidence files exist at HEAD. GitHub activity and CI are still shown."
               | Evidence.CoverageUnavailable reason -> reason) ]

    type Attention = { Id: string; State: string; Label: string; Text: string }

    let attention (model: Model) (snapshot: Snapshot option) : Attention list =
        let sha, _, verdict = headInfo model snapshot
        let failingChecks =
            match snapshot, sha with
            | Some s, Some h ->
                (Status.checksForSha h s.HeadChecks |> List.filter (fun c -> c.Outcome = Failed) |> List.map (fun c -> c.Name))
                @ (Status.runsForSha h s.Runs |> List.filter (fun r -> r.Outcome = Failed) |> List.map (fun r -> r.Name))
                |> List.distinct
            | _ -> []
        let failures =
            failingChecks |> List.map (fun name -> { Id = $"fail:{name}"; State = "blocked"; Label = "Failing on HEAD"; Text = name })
        let blockers =
            snapshot
            |> Option.bind (fun s -> Evidence.requirements s.Evidence)
            |> Option.map (List.filter (fun r -> r.State = Blocked || r.State = FailedEvidence))
            |> Option.defaultValue []
            |> List.truncate 5
            |> List.map (fun r -> { Id = $"req:{r.Id}"; State = "blocked"; Label = (if r.State = Blocked then "Blocked requirement" else "Failed requirement"); Text = $"{r.Id} {r.Title}" })
        let unknown =
            match verdict with
            | Status.HeadUnknown reason when sha.IsSome -> [ { Id = "head-unknown"; State = "unknown"; Label = "HEAD checks unknown"; Text = reason } ]
            | Status.HeadRunning -> [ { Id = "head-running"; State = "attention"; Label = "HEAD checks running"; Text = "Results for the current commit are not complete." } ]
            | _ -> []
        let stale =
            match model.Fetch with
            | Stale(_, reason) -> [ { Id = "stale"; State = "attention"; Label = "Stale data"; Text = reason } ]
            | RateLimited _ -> [ { Id = "rate"; State = "attention"; Label = "Rate limited"; Text = "GitHub's API limit was reached; data may be incomplete." } ]
            | Partial(_, reason) -> [ { Id = "partial"; State = "attention"; Label = "Partial refresh"; Text = reason } ]
            | FetchError reason -> [ { Id = "error"; State = "blocked"; Label = "Refresh failed"; Text = reason } ]
            | _ -> []
        let unverifiedSmoke =
            match snapshot, sha, verdict with
            | Some s, Some h, Status.HeadPassing when Status.smokeChecksForSha h s.HeadChecks s.Runs |> List.isEmpty ->
                [ { Id = "smoke"; State = "unknown"; Label = "Application not verified"; Text = "CI passes for HEAD, but no smoke-test evidence shows the deployed application works." } ]
            | _ -> []
        failures @ blockers @ stale @ unknown @ unverifiedSmoke

    let private attentionItem (a: Attention) =
        item [ pt "id" a.Id; pt "state" a.State; pt "label" a.Label; pt "text" a.Text ]

    // ---- overview ----

    let private overview (model: Model) (snapshot: Snapshot option) (events: SourceEvent list) : Entry list =
        let observed = observedText model snapshot
        let sha, _, verdict = headInfo model snapshot
        let attentionItems = attention model snapshot
        let openPulls = snapshot |> Option.map (fun s -> s.Pulls |> List.filter (fun p -> p.State = PullOpen) |> List.length)
        let headCheckCount = match snapshot, sha with Some s, Some h -> Some(Status.checksForSha h s.HeadChecks |> List.length) | _ -> None
        let requirements = snapshot |> Option.bind (fun s -> Evidence.requirements s.Evidence)
        let verifiedCount = requirements |> Option.map (List.filter (fun r -> r.State = Verified) >> List.length)
        let commitCount = snapshot |> Option.map (fun s -> List.length s.Commits)
        let moreCommits = snapshot |> Option.exists (fun s -> s.MoreCommits)
        let pullsPartial = snapshot |> Option.exists (fun s -> List.length s.Pulls >= GitHub.pageSize)
        let metric id label (value: int option) scope href =
            item
                [ pt "id" id
                  pt "label" label
                  pt "value" (value |> Option.map string |> Option.defaultValue "Unavailable")
                  pt "scope" scope
                  pt "href" href ]
        let metrics =
            [ metric "commits" "Recent commits" commitCount (if moreCommits then $"Loaded window only (partial) · {observed}" else $"All commits on the branch · {observed}") "#/activity"
              metric "pulls" "Open pull requests" openPulls (if pullsPartial then $"Among the {GitHub.pageSize} most recently updated (partial) · {observed}" else $"All pull requests · {observed}") "#/activity"
              metric "checks" "HEAD check runs" headCheckCount (sha |> Option.map (fun h -> $"For commit {Repo.shortSha h} · {observed}") |> Option.defaultValue "HEAD not observed") "#/verification"
              metric "requirements" "Verified requirements" verifiedCount (match requirements with Some r -> $"Of {List.length r} in supported evidence · {observed}" | None -> "No supported requirements evidence") "#/verification" ]
        let activityWindow =
            match events with
            | [] -> "No timestamped events loaded."
            | _ ->
                let times = events |> List.map (fun e -> e.EventTime)
                $"{List.length events} events from {Time.local model.Zone (List.min times)} to {Time.local model.Zone (List.max times)}."
        let coverage = Evidence.coverage (snapshot |> Option.map (fun s -> s.Evidence) |> Option.defaultValue [])
        let reqText =
            match requirements with
            | Some r -> $"{verifiedCount |> Option.defaultValue 0} of {List.length r} requirements verified in supported evidence."
            | None -> "Requirements verification unavailable: no supported evidence."
        [ items "attention" (attentionItems |> List.map attentionItem)
          f "hasAttention" (not attentionItems.IsEmpty)
          f "noAttention" (attentionItems.IsEmpty && snapshot.IsSome)
          f "noAttentionList" attentionItems.IsEmpty
          items "metrics" metrics
          items "recentEvents" (events |> List.truncate 5 |> List.map (eventItem model observed))
          f "hasRecentEvents" (not events.IsEmpty)
          f "noRecentEvents" events.IsEmpty
          t "activitySummary"
              (match snapshot with
               | None -> "Unavailable until GitHub data loads."
               | Some s ->
                   $"{List.length s.Commits} commits, {List.length s.Pulls} pull requests, {List.length s.Runs} workflow runs and {List.length s.Deployments} deployments loaded.")
          t "activityWindow" activityWindow
          t "activityCoverage" (if moreCommits || pullsPartial then "Partial coverage: only the most recent records are loaded." else "Loaded records cover the observed window.")
          t "verificationSummary" $"HEAD checks: {Status.verdictLabel verdict}. {reqText}"
          t "verificationCoverage" $"Evidence coverage: {Evidence.coverageLabel coverage}. Deployed-application smoke test: see Verification."
          t "dimensionHeadState" (Status.verdictState verdict) ]

    // ---- efficiency (shared by Overview and Verification) ----

    let metricInputs (model: Model) (snapshot: Snapshot option) : Metrics.Inputs =
        { Kickoff = (Model.competition model).Kickoff
          Runs = snapshot |> Option.map (fun s -> s.Runs) |> Option.defaultValue []
          RunsTotal = snapshot |> Option.bind (fun s -> s.RunsTotal)
          Deployments = snapshot |> Option.map (fun s -> s.Deployments) |> Option.defaultValue []
          DeploymentStatuses = snapshot |> Option.map (fun s -> s.DeploymentStatuses) |> Option.defaultValue Map.empty
          Requirements = snapshot |> Option.bind (fun s -> Evidence.requirements s.Evidence)
          Corrections = snapshot |> Option.bind (fun s -> Evidence.corrections s.Evidence)
          RunsUnreadable =
            snapshot
            |> Option.bind (fun s ->
                match s.Endpoints |> Map.tryFind "workflow runs" with
                | Some(EndpointFailed r) -> Some r
                | Some EndpointRateLimited -> Some "rate limited"
                | _ -> None) }

    let private efficiency (model: Model) (snapshot: Snapshot option) : Entry list =
        let metrics = if snapshot.IsSome then Metrics.all (metricInputs model snapshot) else []
        let measured = metrics |> List.filter (fun m -> m.Value.IsSome)
        let metricItem (m: Metrics.Metric) =
            item
                [ pt "id" m.Id
                  pt "name" m.Name
                  pt "value" (m.Value |> Option.defaultValue Metrics.notMeasurable)
                  pf "measurable" m.Value.IsSome
                  pt "reason" m.Reason
                  pt "definition" m.Definition
                  pt "denominator" m.Denominator
                  pt "window" m.Window
                  pt "provenance" m.Provenance ]
        [ items "efficiency" (metrics |> List.map metricItem)
          t "efficiencySummary"
              (match metrics, measured with
               | [], _ -> "Not measurable from available evidence."
               | _, [] -> "Not measurable from available evidence."
               | _, ms -> ms |> List.map (fun m -> $"{m.Name}: {m.Value.Value}") |> fun xs -> String.Join(" · ", xs))
          t "efficiencyCoverage" $"{List.length measured} of {List.length metrics} metrics measurable · definitions {Metrics.definitionVersion}"
          t "metricsVersion" Metrics.definitionVersion ]

    // ---- activity ----

    let private branchName (s: Snapshot) = s.Repository |> Option.map (fun r -> r.DefaultBranch) |> Option.defaultValue "the default branch"
    let private shortOr (sha: string option) = sha |> Option.map Repo.shortSha |> Option.defaultValue "?"
    let private scopeText (scope: string option) = scope |> Option.defaultValue "not stated"

    let private filterItems (selected: Events.Filter) (events: SourceEvent list) =
        [ Events.AllEvents, "All"; Events.OnlyCommits, "Commits"; Events.OnlyPulls, "Pull requests"; Events.OnlyChecks, "Checks"; Events.OnlyDeployments, "Deployments" ]
        |> List.map (fun (filter, label) ->
            let count = events |> List.filter (Events.matches filter) |> List.length
            item
                [ pt "key" (Events.filterKey filter)
                  pt "label" $"{label} ({count})"
                  pf "checked" (filter = selected) ])

    let private activity (model: Model) (snapshot: Snapshot option) (events: SourceEvent list) : Entry list =
        let observed = observedText model snapshot
        let visible = events |> List.filter (Events.matches model.Filter)
        let more = snapshot |> Option.exists (fun s -> s.MoreCommits)
        let loadingMore = model.Pending |> Map.exists (fun _ p -> match p.Request with FetchCommits n -> n > 1 | _ -> false)
        let failedEndpoints =
            snapshot
            |> Option.map (fun s -> s.Endpoints |> Map.toList |> List.choose (fun (k, v) -> match v with EndpointFailed r -> Some $"{k} ({r})" | EndpointRateLimited -> Some $"{k} (rate limited)" | _ -> None))
            |> Option.defaultValue []
        [ items "filters" (filterItems model.Filter events)
          items "events" (visible |> List.map (eventItem model observed))
          f "hasEvents" (not visible.IsEmpty)
          f "noEventsList" visible.IsEmpty
          f "noEvents" (visible.IsEmpty && snapshot.IsSome && not (isLoading model))
          f "activityLoading" (snapshot.IsNone || (isLoading model && events.IsEmpty))
          f "hasActivityError" (not failedEndpoints.IsEmpty)
          t "activityError" ("Not shown because GitHub did not return them: " + String.Join(", ", failedEndpoints) + ". Other records are still listed.")
          t "noEventsText" "No activity found in the loaded window."
          t "activityScope"
              (match snapshot with
               | None -> ""
               | Some s ->
                   let commitScope = if s.MoreCommits then $"the newest {List.length s.Commits} commits (more exist)" else $"{List.length s.Commits} commits"
                   $"Showing {List.length visible} of {List.length events} loaded events: {commitScope}, the {GitHub.pageSize} most recently updated pull requests, the latest workflow runs on {branchName s}, HEAD check runs and recent deployments. This is not a complete history.")
          f "canLoadMore" more
          f "cannotLoadMore" (not more)
          f "moreDisabled" (loadingMore || isLoading model)
          t "moreLabel" (if loadingMore then "Loading older commits…" else "Load older commits") ]

    // ---- verification ----

    let private evidenceState (state: EvidenceState) = Evidence.stateLozenge state

    let private verification (model: Model) (snapshot: Snapshot option) : Entry list =
        let sha, _, verdict = headInfo model snapshot
        let repo = model.Selection
        let checks =
            match snapshot, sha with
            | Some s, Some h ->
                let fromChecks =
                    Status.checksForSha h s.HeadChecks
                    |> List.map (fun c -> $"check:{c.Id}", c.Name, "Check run" + (c.App |> Option.map (fun a -> $" · {a}") |> Option.defaultValue ""), c.Outcome, c.HtmlUrl, c.CompletedAt |> Option.orElse c.StartedAt)
                let fromRuns =
                    Status.runsForSha h s.Runs
                    |> List.map (fun r -> $"run:{r.Id}", r.Name, (if r.RunAttempt > 1L then $"Workflow run · attempt {r.RunAttempt}" else "Workflow run"), r.Outcome, r.HtmlUrl, r.UpdatedAt)
                fromChecks @ fromRuns
            | _ -> []
        let order o = match o with Failed -> 0 | Running | Queued -> 1 | Cancelled -> 2 | UnknownOutcome -> 3 | Passed -> 4 | _ -> 5
        let checkItems =
            checks
            |> List.sortBy (fun (_, name, _, o, _, _) -> order o, name)
            |> List.map (fun (id, name, kind, outcome, url, time) ->
                item
                    [ pt "id" id
                      pt "name" name
                      pt "kind" kind
                      pt "outcome" (Status.outcomeLabel outcome)
                      pt "state" (Status.outcomeState outcome)
                      pt "url" (Repo.safeGitHubUrl url |> Option.defaultValue "https://github.com/")
                      pt "time" (time |> Option.map (Time.local model.Zone) |> Option.defaultValue "time not reported") ])
        let truth =
            match snapshot with
            | Some s -> Status.truthRows sha verdict s.Runs s.HeadChecks s.Deployments s.DeploymentStatuses
            | None -> []
        let truthItems =
            truth
            |> List.map (fun r ->
                item
                    [ pt "id" r.Id; pt "label" r.Label; pt "state" r.State; pt "value" r.Value; pt "detail" r.Detail
                      pt "url" (r.Url |> Option.defaultValue ""); pf "hasUrl" r.Url.IsSome ])
        let sources = snapshot |> Option.map (fun s -> s.Evidence) |> Option.defaultValue []
        let requirements = Evidence.requirements sources
        let reqOrder (r: Evidence.Requirement) = match r.State with FailedEvidence -> 0 | Blocked -> 1 | Unverified -> 2 | Unavailable _ -> 3 | Verified -> 4
        let traceUrl = match repo, sha with Some r, Some h -> Evidence.blobUrl r h "evidence/traceability.json" | _ -> ""
        let reqItems =
            requirements
            |> Option.defaultValue []
            |> List.sortBy (fun r -> reqOrder r, r.Id)
            |> List.map (fun r ->
                item
                    [ pt "id" r.Id
                      pt "title" (orDash r.Title)
                      pt "state" (evidenceState r.State)
                      pt "label" (Evidence.stateLabel r.State)
                      pt "raw" ("Source status: " + r.RawStatus + (r.Priority |> Option.map (fun p -> " · " + p) |> Option.defaultValue ""))
                      pt "evidence" (if r.Evidence = "" then "No evidence text recorded." else r.Evidence)
                      pt "note" r.Note
                      pf "hasNote" (r.Note <> "")
                      pt "url" traceUrl ])
        let counts =
            match requirements with
            | None -> []
            | Some rs ->
                [ "Verified", "ok", (fun (r: Evidence.Requirement) -> r.State = Verified)
                  "Failed", "blocked", (fun r -> r.State = FailedEvidence)
                  "Blocked", "blocked", (fun r -> r.State = Blocked)
                  "Unverified", "attention", (fun r -> r.State = Unverified)
                  "Unavailable", "unknown", (fun r -> match r.State with Unavailable _ -> true | _ -> false) ]
                |> List.map (fun (label, state, pred) -> item [ pt "id" label; pt "label" label; pt "state" state; pt "count" (string (rs |> List.filter pred |> List.length)) ])
        let acceptanceItems =
            Evidence.acceptance sources
            |> Option.defaultValue []
            |> List.map (fun a ->
                item
                    [ pt "id" a.Id
                      pt "requirement" (a.Requirement |> Option.defaultValue "")
                      pt "state" (evidenceState a.State)
                      pt "label" (Evidence.stateLabel a.State)
                      pt "raw" a.RawResult
                      pt "note" (if a.Note = "" then (String.Join(", ", a.Runs)) else a.Note) ])
        let decisionsUrl = match repo, sha with Some r, Some h -> Evidence.blobUrl r h "evidence/DECISIONS.md" | _ -> ""
        let defectsUrl = match repo, sha with Some r, Some h -> Evidence.blobUrl r h "evidence/defects.md" | _ -> ""
        let decisionItems =
            Evidence.decisions sources
            |> Option.defaultValue []
            |> List.map (fun d -> item [ pt "id" d.Id; pt "text" d.Text; pt "why" d.Why; pt "effect" d.Effect; pt "url" decisionsUrl ])
        let correctionItems =
            Evidence.corrections sources
            |> Option.defaultValue []
            |> List.map (fun c -> item [ pt "id" c.Id; pt "defect" c.Defect; pt "foundBy" c.FoundBy; pt "fix" c.Fix; pt "guard" c.Guard; pt "url" defectsUrl ])
        let sourceItems =
            Evidence.registry
            |> List.map (fun a ->
                let source = sources |> List.tryFind (fun s -> s.Adapter.Id = a.Id)
                let outcome = source |> Option.map (fun s -> s.Outcome) |> Option.defaultValue Evidence.NotFetched
                let url = match repo, sha with Some r, Some h -> Evidence.blobUrl r h a.Path | _ -> ""
                item
                    [ pt "id" a.Id
                      pt "path" a.Path
                      pt "schema" a.Schema
                      pt "description" a.Description
                      pt "state" (Evidence.outcomeLozenge outcome)
                      pt "label" (Evidence.outcomeLabel outcome)
                      pt "url" url
                      pf "hasUrl" (url <> "" && (match outcome with Evidence.Missing | Evidence.NotFetched -> false | _ -> true)) ])
        let blockers =
            attention model snapshot |> List.filter (fun a -> a.State = "blocked")
        [ items "headChecks" checkItems
          f "hasHeadChecks" (not checkItems.IsEmpty)
          f "noHeadChecksList" checkItems.IsEmpty
          f "noHeadChecks" (checkItems.IsEmpty && snapshot.IsSome)
          t "noChecksText"
              (match snapshot |> Option.map unreadableCi with
               | Some(_ :: _ as sources) -> String.Join(" and ", sources) + " could not be read in the last refresh, so HEAD checks are unknown."
               | _ -> "No check runs or workflow runs were found for the current HEAD commit.")
          t "checksScope"
              (match snapshot with
               | Some s when s.ChecksTotal |> Option.exists (fun total -> total > int64 (List.length s.HeadChecks)) ->
                   $"Partial: {List.length s.HeadChecks} of {s.ChecksTotal.Value} check runs loaded."
               | _ -> "Only runs whose head SHA equals HEAD are listed.")
          items "truth" truthItems
          items "blockers" (blockers |> List.map attentionItem)
          f "hasBlockers" (not blockers.IsEmpty)
          f "noBlockersList" blockers.IsEmpty
          f "noBlockers" (blockers.IsEmpty && snapshot.IsSome)
          f "hasRequirements" requirements.IsSome
          f "noRequirements" requirements.IsNone
          t "requirementsUnavailable"
              (match sources |> List.tryFind (fun s -> s.Adapter.Id = "traceability") |> Option.map (fun s -> s.Outcome) with
               | Some(Evidence.Unsupported found) -> $"Structured verification unavailable: evidence/traceability.json uses an unsupported schema ({found}). Supported: causa.traceability/1."
               | Some(Evidence.Invalid reason) -> $"Structured verification unavailable: evidence/traceability.json could not be parsed ({reason})."
               | Some(Evidence.FetchFailed reason) -> $"Structured verification unavailable: evidence could not be read ({reason})."
               | _ -> "Structured verification unavailable: no supported requirements evidence was found at HEAD.")
          t "requirementsScope"
              (sources
               |> List.tryPick (fun s -> match s.Outcome with Evidence.Parsed(Evidence.Requirements(scope, rs)) -> Some(scope, rs) | _ -> None)
               |> Option.map (fun (scope, rs) -> $"{List.length rs} requirements from evidence/traceability.json (causa.traceability/1) at {shortOr sha}. Scope: {scopeText scope}. Statuses are recorded by the observed repository, not by Specula.")
               |> Option.defaultValue "")
          t "requirementsUrl" (if traceUrl = "" then "https://github.com/" else traceUrl)
          items "requirementCounts" counts
          items "requirements" reqItems
          items "acceptance" acceptanceItems
          f "hasAcceptance" (not acceptanceItems.IsEmpty)
          f "noAcceptance" acceptanceItems.IsEmpty
          items "decisions" decisionItems
          f "hasDecisions" (not decisionItems.IsEmpty)
          f "noDecisions" decisionItems.IsEmpty
          items "corrections" correctionItems
          f "hasCorrections" (not correctionItems.IsEmpty)
          f "noCorrections" correctionItems.IsEmpty
          items "evidenceSources" sourceItems
          t "adapterVersion" Evidence.adapterVersion ]

    // ---- replay ----

    let private replay (model: Model) (snapshot: Snapshot option) (events: SourceEvent list) : Entry list =
        let observed = observedText model snapshot
        let r = model.Replay
        let start = Time.parseLocalInput model.Zone r.StartText
        let finish = Time.parseLocalInput model.Zone r.EndText
        let validation =
            match r.StartText.Trim(), r.EndText.Trim(), start, finish with
            | "", _, _, _ | _, "", _, _ -> Some "Choose a start and an end to replay a time window."
            | _, _, None, _ -> Some "The start is not a valid date and time."
            | _, _, _, None -> Some "The end is not a valid date and time."
            | _, _, Some s, Some e when e <= s -> Some "The end must be after the start."
            | _ -> None
        let windowEvents =
            match start, finish, validation with
            | Some s, Some e, None -> events |> List.filter (Events.inWindow s e) |> List.filter (Events.matches r.Filter) |> List.sortBy (fun e -> e.EventTime, e.StableId)
            | _ -> []
        let competition = Model.competition model
        let clock = Metrics.clock model.Now competition
        let milestones =
            match snapshot with
            | None -> []
            | Some s ->
                let headDate = s.HeadSha |> Option.bind (fun h -> s.Commits |> List.tryFind (fun c -> c.Sha = h)) |> Option.bind (fun c -> c.Date)
                Metrics.milestones
                    competition
                    { Repository = s.Repository
                      HeadDate = headDate
                      Kickoff = competition.Kickoff
                      Runs = s.Runs
                      DeploymentStatuses = s.DeploymentStatuses
                      Deployments = s.Deployments
                      RequirementsFound = (Evidence.requirements s.Evidence).IsSome
                      EvidenceUrl = match model.Selection, s.HeadSha with Some repo, Some h -> Some(Evidence.blobUrl repo h "evidence/traceability.json") | _ -> None }
        let milestoneItem (m: Metrics.Milestone) =
            let planned = m.Planned |> Option.map (fun p -> $"Planned {Time.local model.Zone p}") |> Option.defaultValue "No planned checkpoint"
            let observedText, state, source, url =
                match m.Observation with
                | Metrics.Observed(at, src, url) ->
                    let late = m.Planned |> Option.exists (fun p -> at > p)
                    $"Observed {Time.local model.Zone at}" + (if late then " (after the planned time)" else ""), (if late then "attention" else "ok"), src, url
                | Metrics.NotObserved reason -> "Not observed", "unknown", reason, None
            item
                [ pt "id" (Preferences.milestoneKey m.Kind)
                  pt "label" (Preferences.milestoneLabel m.Kind)
                  pt "planned" planned
                  pf "isPlanned" m.Planned.IsSome
                  pt "observed" observedText
                  pt "state" state
                  pt "source" source
                  pt "url" (url |> Option.bind Repo.safeGitHubUrl |> Option.defaultValue "")
                  pf "hasUrl" (url |> Option.bind Repo.safeGitHubUrl).IsSome ]
        let sha, _, verdict = headInfo model snapshot
        let fixes =
            Metrics.recoveries (snapshot |> Option.map (fun s -> s.Runs) |> Option.defaultValue [])
            |> List.map (fun (failed, passed, span) ->
                item
                    [ pt "id" $"{failed.Id}-{passed.Id}"
                      pt "text" $"\"{failed.Name}\" failed on {Repo.shortSha failed.HeadSha}, then passed on {Repo.shortSha passed.HeadSha} after {Time.duration span}."
                      pt "failedUrl" failed.HtmlUrl
                      pt "passedUrl" passed.HtmlUrl ])
        [ t "replayStart" r.StartText
          t "replayEnd" r.EndText
          f "hasReplayError" (r.Error.IsSome || (validation.IsSome && (r.StartText <> "" || r.EndText <> "")))
          t "replayError" (r.Error |> Option.orElse validation |> Option.defaultValue "")
          f "replayPrompt" (r.StartText = "" && r.EndText = "")
          items "replayFilters" (filterItems r.Filter (match start, finish with Some s, Some e when e > s -> events |> List.filter (Events.inWindow s e) | _ -> []))
          items "replayEvents" (windowEvents |> List.map (eventItem model observed))
          f "hasReplayEvents" (not windowEvents.IsEmpty)
          f "noReplayEventsList" windowEvents.IsEmpty
          f "noReplayEvents" (windowEvents.IsEmpty && validation.IsNone)
          t "replayCount" (if validation.IsNone then $"{List.length windowEvents} events in the window, oldest first. Only loaded, timestamped GitHub records are included; this is not a record of agent reasoning or local work." else "")
          items "milestones" (milestones |> List.map milestoneItem)
          f "hasClock" clock.IsSome
          f "noClock" clock.IsNone
          f "hideClock" clock.IsNone
          t "clockKickoff" (clock |> Option.map (fun c -> Time.local model.Zone c.Kickoff) |> Option.defaultValue "")
          t "clockDeadline" (clock |> Option.map (fun c -> Time.local model.Zone c.Deadline) |> Option.defaultValue "")
          t "clockElapsed" (clock |> Option.map (fun c -> Time.duration c.Elapsed) |> Option.defaultValue "")
          t "clockRemaining" (clock |> Option.map (fun c -> if c.Passed then "Deadline passed" else Time.duration c.Remaining) |> Option.defaultValue "")
          t "clockAsOf" $"As of {Time.local model.Zone model.Now} by this device's clock, which may be wrong. Updates when you interact or refresh."
          f "clockPassed" (clock |> Option.exists (fun c -> c.Passed))
          f "presentation" r.Presentation
          f "notPresentation" (not r.Presentation)
          t "presentationLabel" (if r.Presentation then "Exit presentation view" else "Presentation view")
          t "presentationPressed" (aria r.Presentation)
          t "presentHead" (sha |> Option.map (fun h -> $"HEAD {Repo.shortSha h}: checks {Status.verdictLabel verdict}") |> Option.defaultValue "HEAD not observed")
          t "presentHeadState" (Status.verdictState verdict)
          items "presentFixes" fixes
          f "hasPresentFixes" (not fixes.IsEmpty)
          f "noPresentFixes" fixes.IsEmpty ]

    // ---- settings and about ----

    let private settings (model: Model) (snapshot: Snapshot option) : Entry list =
        let c = Model.competition model
        let recorded = snapshot |> Option.bind (fun s -> Evidence.kickoff s.Evidence)
        let checkpointItems =
            Preferences.milestoneKinds
            |> List.filter (fun k -> k <> RepositoryCreated)
            |> List.map (fun k ->
                item
                    [ pt "key" (Preferences.milestoneKey k)
                      pt "label" (Preferences.milestoneLabel k)
                      pt "inputId" ("cp-" + Preferences.milestoneKey k)
                      pt "offset" (c.Checkpoints |> List.tryFind (fun cp -> cp.Milestone = k) |> Option.map (fun cp -> string cp.OffsetMinutes) |> Option.defaultValue "") ])
        [ t "kickoffValue" (if model.Competition.KickoffText <> "" then model.Competition.KickoffText else c.Kickoff |> Option.map (Time.toLocalInput model.Zone) |> Option.defaultValue "")
          t "durationValue" (if model.Competition.DurationText <> "" then model.Competition.DurationText else string c.DurationMinutes)
          t "kickoffSaved" (c.Kickoff |> Option.map (fun k -> $"Saved kickoff: {Time.local model.Zone k} ({Time.utc k})") |> Option.defaultValue "No kickoff saved. Time-based metrics stay unmeasurable until one is set.")
          f "hasCompetitionMessage" model.Competition.Message.IsSome
          t "competitionMessage" (model.Competition.Message |> Option.defaultValue "")
          items "checkpoints" checkpointItems
          f "hasRecordedKickoff" recorded.IsSome
          t "recordedKickoff" (recorded |> Option.map (fun k -> $"evidence/kickoff/kickoff.json records kickoff {Time.utc k.T0}" + (k.LimitMinutes |> Option.map (fun m -> $" with a {m}-minute limit") |> Option.defaultValue "") + ".") |> Option.defaultValue "")
          t "themeValue" (Preferences.themeKey model.Prefs.Theme)
          t "storageStatus"
              (match model.Storage with
               | StorageAvailable -> $"Saved in this browser under \"{Preferences.storageKey}\" (format version {Preferences.currentVersion}): selected and recent repositories, last view, theme and competition settings. No credentials or GitHub data are stored."
               | SessionOnly why -> $"Not saved: {why}. Settings last only for this session."
               | StorageUnknown -> "Checking browser storage…") ]

    let private about (model: Model) (snapshot: Snapshot option) : Entry list =
        let deps = dependencies |> List.map (fun (id, name, version, url) -> item [ pt "id" id; pt "name" name; pt "version" version; pt "url" url ])
        let adapters =
            Evidence.registry
            |> List.map (fun a -> item [ pt "id" a.Id; pt "path" a.Path; pt "schema" a.Schema ])
        let endpoints =
            snapshot
            |> Option.map (fun s ->
                s.Endpoints
                |> Map.toList
                |> List.map (fun (k, v) ->
                    let state, label =
                        match v with
                        | EndpointOk -> "unknown", "Read"
                        | EndpointEmpty r -> "unknown", $"Empty: {r}"
                        | EndpointFailed r -> "attention", $"Failed: {r}"
                        | EndpointRateLimited -> "blocked", "Rate limited"
                    item [ pt "id" k; pt "name" k; pt "state" state; pt "label" label ]))
            |> Option.defaultValue []
        [ t "version" BuildInfo.Version
          t "buildTime" BuildInfo.BuildTime
          t "sourceUrl" (if BuildInfo.SourceSha.Length = 40 then $"https://github.com/kemiller2002/specula/commit/{BuildInfo.SourceSha}" else "https://github.com/kemiller2002/specula")
          items "dependencies" deps
          items "adapters" adapters
          items "endpoints" endpoints
          f "hasEndpoints" (not endpoints.IsEmpty)
          t "requestCount" $"{model.RequestCount} GitHub requests issued in this session (304 Not Modified answers do not count against GitHub's limit)." ]

    let project (model: Model) : ViewState =
        let snapshot = Model.currentSnapshot model
        let events = snapshot |> Option.map Model.events |> Option.defaultValue []
        [ shell model
          picker model
          headEntries model snapshot
          coverageEntries snapshot
          overview model snapshot events
          efficiency model snapshot
          activity model snapshot events
          verification model snapshot
          replay model snapshot events
          settings model snapshot
          about model snapshot ]
        |> List.concat
        |> Map.ofList
