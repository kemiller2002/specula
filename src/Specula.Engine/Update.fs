namespace Specula.Engine

open System
open System.Text.Json
open Limen.Contract
open Limen.Contract.Core
open Specula.Engine.Model
open Specula.Engine.Preferences

/// Pure transitions: (message, model) -> (model, effects, cancellations).
/// No browser API is touched here; HTTP and storage are requested as Limen
/// effects and their results come back as messages.
module Update =

    type Msg =
        | Init of BrowserLocation
        | LocationChanged of BrowserLocation
        | DraftChanged of string
        | Observe
        | ChooseRecent of string
        | Refresh
        | LoadMoreCommits
        | SetFilter of string
        | ToggleEvent of string
        | Forget
        | ReplayStart of string
        | ReplayEnd of string
        | ReplayFilter of string
        | ReplayUseCompetition
        | TogglePresentation
        | KickoffChanged of string
        | DurationChanged of string
        | CheckpointChanged of milestone: string * value: string
        | SaveCompetition
        | UseRecordedKickoff
        | ClearCompetition
        | SetTheme of string
        | ClearAllSettings
        | SaveSharedAsDefault
        | DismissNotice
        | HttpDone of string * EffectOutcome
        | StorageDone of string * StorageOutcome
        | Ignored

    [<NoComparison>]
    type Step =
        { Model: Model
          Effects: EffectRequest list
          Cancellations: CorrelationId list }

    let private just model = { Model = model; Effects = []; Cancellations = [] }

    let private andThen (f: Model -> Step) (step: Step) : Step =
        let next = f step.Model
        { Model = next.Model
          Effects = step.Effects @ next.Effects
          Cancellations = step.Cancellations @ next.Cancellations }

    // ---- effect construction ----

    let private nextId (model: Model) =
        let n = model.Sequence + 1
        $"c{n}", { model with Sequence = n }

    let private register (pending: Pending) (effectOf: CorrelationId -> EffectRequest) (model: Model) : Step =
        let id, model = nextId model
        { Model = { model with Pending = model.Pending |> Map.add id pending; RequestCount = model.RequestCount + (if pending.Url.IsSome then 1 else 0) }
          Effects = [ effectOf (CorrelationId id) ]
          Cancellations = [] }

    let private apiHeaders (etag: string option) =
        [ "accept", "application/vnd.github+json"
          "x-github-api-version", "2022-11-28" ]
        @ (etag |> Option.map (fun e -> [ "if-none-match", e ]) |> Option.defaultValue [])
        |> Map.ofList

    let private httpEffect (url: string) (kind: HttpResponseKind) (headers: Map<string, string> option) (cid: CorrelationId) =
        EffectRequest.Http
            { CorrelationId = cid
              Method = HttpMethod.Get
              Url = url
              Headers = headers
              Body = None
              TimeoutMs = 20000L
              Response = Some kind
              ResponseHeaders = Some GitHub.responseHeaders
              Credentials = Some HttpCredentials.Omit
              Xsrf = None }

    let private requestUrl (repo: RepoId) (snapshot: Snapshot option) (request: Request) : (string * HttpResponseKind) option =
        let u = GitHub.urls repo
        let branch = snapshot |> Option.bind (fun s -> s.Repository) |> Option.map (fun r -> r.DefaultBranch)
        match request with
        | FetchRepository -> Some(u.Repository, HttpResponseKind.Json)
        | FetchCommits page -> branch |> Option.map (fun b -> u.Commits b page, HttpResponseKind.Json)
        | FetchPulls -> Some(u.Pulls, HttpResponseKind.Json)
        | FetchRuns -> branch |> Option.map (fun b -> u.Runs b, HttpResponseKind.Json)
        | FetchDeployments -> Some(u.Deployments, HttpResponseKind.Json)
        | FetchHeadChecks sha -> Some(u.CheckRuns sha, HttpResponseKind.Json)
        | FetchDeploymentStatuses id -> Some(u.DeploymentStatuses id, HttpResponseKind.Json)
        | RecheckHead -> branch |> Option.map (fun b -> u.Branch b, HttpResponseKind.Json)
        | FetchEvidence(adapterId, sha) ->
            Evidence.registry
            |> List.tryFind (fun a -> a.Id = adapterId)
            |> Option.map (fun a -> Evidence.rawUrl repo sha a.Path, HttpResponseKind.Text)
        | LoadPreferences | SavePreferences -> None

    let private fetch (repo: RepoId) (request: Request) (model: Model) : Step =
        let snapshot = match model.Snapshot with Some s when RepoId.same s.Repo repo -> Some s | _ -> None
        match requestUrl repo snapshot request with
        | None -> just model
        | Some(url, kind) ->
            let headers =
                match kind with
                | HttpResponseKind.Json -> Some(apiHeaders (model.Etags |> Map.tryFind url |> Option.map fst))
                | _ -> None // raw.githubusercontent.com: a simple CORS request
            model
            |> register { Request = request; Repo = Some repo; Generation = model.Generation; Url = Some url } (httpEffect url kind headers)

    let private fetchAll (repo: RepoId) (requests: Request list) (model: Model) : Step =
        requests |> List.fold (fun step r -> step |> andThen (fetch repo r)) (just model)

    let private savePrefs (model: Model) : Step =
        match model.Storage with
        | SessionOnly _ -> just model
        | _ ->
            let value = Preferences.encode model.Prefs
            model
            |> register { Request = SavePreferences; Repo = None; Generation = model.Generation; Url = None } (fun cid ->
                EffectRequest.Storage(StorageEffectRequest.Set(cid, Preferences.storageKey, value)))

    let private navigate (screen: Screen) (model: Model) : Step =
        let url = model.Path + "#/" + screenKey screen
        let id, model = nextId model
        // Navigation results carry no application work, so they are not tracked.
        { Model = { model with Screen = screen; Route = Some screen }
          Effects = [ EffectRequest.Navigation(NavigationEffectRequest.Replace(CorrelationId id, url)) ]
          Cancellations = [] }

    // ---- refresh lifecycle ----

    let private httpPending (model: Model) =
        model.Pending |> Map.filter (fun _ p -> p.Url.IsSome)

    /// Supersede every in-flight HTTP request (repository switch or forget).
    let private cancelHttp (model: Model) : Step =
        let ids = httpPending model |> Map.toList |> List.map fst
        { Model = { model with Pending = model.Pending |> Map.filter (fun _ p -> p.Url.IsNone) }
          Effects = []
          Cancellations = ids |> List.map CorrelationId }

    let private startRefresh (repo: RepoId) (asCandidate: bool) (model: Model) : Step =
        let keep = match model.Snapshot with Some s when RepoId.same s.Repo repo -> Some s | _ -> None
        cancelHttp model
        |> andThen (fun m ->
            let m =
                { m with
                    Generation = m.Generation + 1
                    Fetch = Loading
                    LastAttempt = Some m.Now
                    Notice = None
                    Candidate = (if asCandidate then Some repo else None)
                    Snapshot = (if asCandidate then m.Snapshot else Some(keep |> Option.defaultValue (emptySnapshot repo))) }
            fetch repo FetchRepository m)

    let private rateLimitBlocked (model: Model) =
        model.RetryAt |> Option.exists (fun t -> t > model.Now)

    let private refresh (model: Model) : Step =
        match model.Selection with
        | None -> just model
        | Some _ when isLoading model -> just { model with Notice = Some "A refresh is already in progress." }
        | Some _ when rateLimitBlocked model ->
            let until = model.RetryAt |> Option.map (Time.local model.Zone) |> Option.defaultValue "later"
            just { model with Notice = Some $"GitHub's rate limit applies until {until}. Refresh was not sent, to avoid exhausting the remaining quota." }
        | Some repo -> startRefresh repo false model

    let private withSnapshot (f: Snapshot -> Snapshot) (model: Model) =
        { model with Snapshot = model.Snapshot |> Option.map f }

    let private setEndpoint key status (s: Snapshot) =
        { s with Endpoints = s.Endpoints |> Map.add key status }

    let private stageOf request =
        match request with
        | FetchRepository -> 1
        | FetchCommits 1 | FetchPulls | FetchRuns | FetchDeployments -> 2
        | FetchHeadChecks _ | FetchDeploymentStatuses _ | FetchEvidence _ -> 3
        | RecheckHead -> 4
        | FetchCommits _ -> 0
        | LoadPreferences | SavePreferences -> 0

    let private finalize (model: Model) : Model =
        match currentSnapshot model with
        | None -> model
        | Some s ->
            let problems =
                s.Endpoints
                |> Map.toList
                |> List.choose (fun (k, v) ->
                    match v with
                    | EndpointFailed r -> Some $"{k}: {r}"
                    | EndpointRateLimited -> Some $"{k}: rate limited"
                    | _ -> None)
            let moved =
                match s.HeadSha, s.RecheckSha with
                | Some a, Some b when not (String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) ->
                    Some $"HEAD moved during refresh ({Repo.shortSha a} → {Repo.shortSha b}); results may mix commits. Refresh again."
                | _ -> None
            let state =
                match moved, problems with
                | Some reason, _ -> Partial(model.Now, reason)
                | None, [] -> Fresh model.Now
                | None, ps -> Partial(model.Now, "Some sources could not be read: " + String.Join("; ", ps))
            { model with
                Fetch = state
                LastSuccess = Some model.Now
                Snapshot = Some { s with ObservedAt = Some model.Now } }

    let private stage3 (repo: RepoId) (model: Model) : Step =
        match currentSnapshot model with
        | None -> just model
        | Some s ->
            let statusRequests =
                s.Deployments
                |> List.sortByDescending (fun d -> d.CreatedAt)
                |> List.truncate 3
                |> List.map (fun d -> FetchDeploymentStatuses d.Id)
            let headRequests =
                match s.HeadSha with
                | Some sha -> FetchHeadChecks sha :: (Evidence.registry |> List.map (fun a -> FetchEvidence(a.Id, sha)))
                | None -> []
            let model =
                match s.HeadSha with
                | Some _ -> model
                | None -> model |> withSnapshot (fun s -> { s with Evidence = [] })
            fetchAll repo (headRequests @ statusRequests) model

    /// Called after each response: when the current stage has no requests
    /// left in flight, issue the next stage or finish.
    let private advance (completedStage: int) (model: Model) : Step =
        let inFlight =
            httpPending model
            |> Map.exists (fun _ p -> p.Generation = model.Generation && stageOf p.Request >= 1)
        match model.Selection, inFlight, completedStage with
        | _, true, _ -> just model
        | None, _, _ -> just model
        | Some _, false, 0 -> just model
        | Some repo, false, 2 ->
            let step = stage3 repo model
            if List.isEmpty step.Effects then
                match currentSnapshot step.Model |> Option.bind (fun s -> s.HeadSha) with
                | Some _ -> fetch repo RecheckHead step.Model
                | None -> just (finalize step.Model)
            else step
        | Some repo, false, 3 -> fetch repo RecheckHead model
        | Some _, false, 4 -> just (finalize model)
        | Some _, false, _ -> just model

    // ---- response classification ----

    [<NoComparison>]
    type private Fetched =
        | Got of JsonElement
        | GotText of string
        | Gone
        | EmptyRepo of string
        | Limited of DateTimeOffset option
        | Offline
        | FailedWith of string
        | Superseded

    let private headerMap (headers: Map<string, string> option) = headers |> Option.defaultValue Map.empty

    let private rememberRateLimit (headers: Map<string, string>) (model: Model) =
        match GitHub.rateLimitOf headers with
        | Some rl -> { model with RateLimit = Some rl }
        | None -> model

    let private resolve (pending: Pending) (outcome: EffectOutcome) (model: Model) : Model * Fetched =
        let isText = match pending.Request with FetchEvidence _ -> true | _ -> false
        let cached () = pending.Url |> Option.bind (fun u -> model.Etags |> Map.tryFind u) |> Option.bind (snd >> Json.tryParse)
        match outcome with
        | EffectOutcome.Success(status, RawJson body, headers) ->
            let headers = headerMap headers
            let model = rememberRateLimit headers model
            let parsed = Json.tryParse body
            if isText then
                match status, parsed |> Option.bind Json.asString with
                | 200L, Some text -> model, GotText text
                | 404L, _ -> model, Gone
                | 429L, _ | 403L, _ -> model, Limited None
                | s, _ -> model, FailedWith $"HTTP {s}"
            else
                match GitHub.classify model.Now status parsed headers with
                | GitHub.Body json ->
                    let model =
                        match GitHub.headerValue "etag" headers, pending.Url with
                        | Some etag, Some url -> { model with Etags = model.Etags |> Map.add url (etag, body) |> fun m -> if Map.count m > 200 then Map.empty |> Map.add url (etag, body) else m }
                        | _ -> model
                    model, Got json
                | GitHub.NotModified ->
                    match cached () with
                    | Some json -> model, Got json
                    | None -> model, FailedWith "Not modified, but no cached copy is held"
                | GitHub.NotFound -> model, Gone
                | GitHub.Conflict message -> model, EmptyRepo message
                | GitHub.Forbidden(true, retryAt, _) -> { model with RetryAt = retryAt |> Option.orElse (Some(model.Now.AddMinutes 1.0)) }, Limited retryAt
                | GitHub.Forbidden(false, _, message) -> model, FailedWith (if message = "" then "Access refused (403)" else "Access refused (403): " + message)
                | GitHub.ServerError s -> model, FailedWith $"GitHub server error ({s})"
                | GitHub.Malformed s -> model, FailedWith $"Unexpected response (HTTP {s})"
        | EffectOutcome.Failure(HttpFailureReason.InvalidResponse, Some 304L) ->
            match cached () with
            | Some json -> model, Got json
            | None -> model, FailedWith "Not modified, but no cached copy is held"
        | EffectOutcome.Failure(HttpFailureReason.Network, _) -> model, Offline
        | EffectOutcome.Failure(HttpFailureReason.Aborted, _) -> model, Superseded
        | EffectOutcome.Failure(HttpFailureReason.TooLarge, _) -> model, FailedWith "Response too large"
        | EffectOutcome.Failure(HttpFailureReason.InvalidResponse, s) ->
            model, FailedWith (s |> Option.map (fun x -> $"Malformed response (HTTP {x})") |> Option.defaultValue "Malformed response")
        | EffectOutcome.Cancelled -> model, Superseded
        | EffectOutcome.OutcomeUnknown _ -> model, Offline

    let private endpointKey request =
        match request with
        | FetchRepository -> "repository"
        | FetchCommits _ -> "commits"
        | FetchPulls -> "pull requests"
        | FetchRuns -> "workflow runs"
        | FetchDeployments -> "deployments"
        | FetchHeadChecks _ -> "HEAD check runs"
        | FetchDeploymentStatuses _ -> "deployment statuses"
        | FetchEvidence(id, _) -> $"evidence:{id}"
        | RecheckHead -> "HEAD recheck"
        | LoadPreferences | SavePreferences -> "storage"

    let private failureStatus fetched =
        match fetched with
        | Limited _ -> EndpointRateLimited
        | Offline -> EndpointFailed "network unavailable"
        | FailedWith reason -> EndpointFailed reason
        | Gone -> EndpointEmpty "not found"
        | EmptyRepo m -> EndpointEmpty m
        | _ -> EndpointFailed "unexpected"

    // ---- stage handlers ----

    let private onRepository (pending: Pending) (fetched: Fetched) (model: Model) : Step =
        let repo = pending.Repo.Value
        let asCandidate = model.Candidate |> Option.exists (RepoId.same repo)
        let parsed = match fetched with Got json -> GitHub.parseRepository repo json | _ -> None
        match fetched, parsed with
        | Got _, Some repository when repository.IsPrivate ->
            just { model with Fetch = FetchError "This repository is private. Specula observes public repositories only."; Candidate = None; DraftError = (if asCandidate then Some "This repository is private. Specula observes public repositories only." else model.DraftError) }
        | Got _, Some repository ->
            let canonical = repository.Id
            let snapshot =
                match model.Snapshot with
                | Some s when RepoId.same s.Repo canonical && not asCandidate -> { s with Repo = canonical; Repository = Some repository; Endpoints = Map.empty |> Map.add "repository" EndpointOk }
                | _ -> { emptySnapshot canonical with Repository = Some repository; Endpoints = Map.empty |> Map.add "repository" EndpointOk }
            let model =
                { model with
                    Selection = Some canonical
                    Candidate = None
                    Snapshot = Some snapshot
                    DraftError = None
                    Draft = (if asCandidate then "" else model.Draft)
                    Prefs = (model.Prefs |> Preferences.withRecent canonical |> fun p -> { p with SelectedRepository = (if model.SharedLink.IsSome && not asCandidate then p.SelectedRepository else Some canonical) })
                    SharedLink = (if asCandidate then None else model.SharedLink) }
            let stage2 = fetchAll canonical [ FetchCommits 1; FetchPulls; FetchRuns; FetchDeployments ]
            if asCandidate then
                savePrefs model |> andThen (navigate Overview) |> andThen stage2
            else
                savePrefs model |> andThen stage2
        | Got _, None ->
            just { model with Fetch = FetchError "GitHub returned repository metadata Specula could not read."; Candidate = None }
        | Gone, _ ->
            if asCandidate then
                just { model with Candidate = None; Fetch = (if model.LastSuccess.IsSome then Stale(model.LastSuccess, "Repository switch was cancelled.") else Idle); DraftError = Some $"{RepoId.text repo} was not found. It may be private, renamed or deleted." }
            else
                just { model with Fetch = FetchError $"{RepoId.text repo} was not found. It may have been made private, renamed or deleted." }
        | Limited retry, _ ->
            let until = retry |> Option.map (Time.local model.Zone) |> Option.defaultValue "a few minutes"
            just
                { model with
                    Candidate = None
                    Fetch = RateLimited retry
                    DraftError = (if asCandidate then Some $"GitHub's unauthenticated rate limit is exhausted. Try again after {until}." else model.DraftError) }
        | Offline, _ ->
            just
                { model with
                    Candidate = None
                    Fetch = Stale(model.LastSuccess, "GitHub could not be reached. Check the network connection.")
                    DraftError = (if asCandidate then Some "GitHub could not be reached. Check the network connection and try again." else model.DraftError) }
        | FailedWith reason, _ ->
            just { model with Candidate = None; Fetch = FetchError reason; DraftError = (if asCandidate then Some reason else model.DraftError) }
        | EmptyRepo reason, _ ->
            just { model with Candidate = None; Fetch = FetchError reason }
        | Superseded, _
        | GotText _, _ -> just model

    let private applyStage (pending: Pending) (fetched: Fetched) (model: Model) : Model =
        let repo = pending.Repo.Value
        let key = endpointKey pending.Request
        let ok f = model |> withSnapshot (f >> setEndpoint key EndpointOk)
        let fail () = model |> withSnapshot (setEndpoint key (failureStatus fetched))
        match pending.Request, fetched with
        | FetchCommits page, Got json ->
            let commits = GitHub.parseCommits repo json
            ok (fun s ->
                { s with
                    Commits = (if page = 1 then Events.merge (fun (c: Commit) -> c.Sha) [] commits else Events.merge (fun (c: Commit) -> c.Sha) s.Commits commits |> List.sortByDescending (fun c -> c.Date))
                    CommitPage = max page s.CommitPage
                    MoreCommits = List.length commits >= GitHub.pageSize && page < GitHub.maxCommitPages
                    HeadSha = (if page = 1 then commits |> List.tryHead |> Option.map (fun c -> c.Sha) else s.HeadSha) })
        | FetchCommits 1, EmptyRepo message ->
            model |> withSnapshot (fun s -> { s with Commits = []; HeadSha = None; MoreCommits = false } |> setEndpoint key (EndpointEmpty message))
        | FetchPulls, Got json -> ok (fun s -> { s with Pulls = GitHub.parsePulls repo json })
        | FetchRuns, Got json ->
            let runs, total = GitHub.parseRuns repo json
            ok (fun s -> { s with Runs = runs; RunsTotal = total })
        | FetchDeployments, Got json -> ok (fun s -> { s with Deployments = GitHub.parseDeployments json })
        | FetchHeadChecks sha, Got json ->
            let checks, total = GitHub.parseCheckRuns repo json
            ok (fun s -> if s.HeadSha = Some sha then { s with HeadChecks = checks; ChecksTotal = total } else s)
        | FetchDeploymentStatuses id, Got json ->
            ok (fun s -> { s with DeploymentStatuses = s.DeploymentStatuses |> Map.add id (GitHub.parseDeploymentStatuses json) })
        | RecheckHead, Got json -> ok (fun s -> { s with RecheckSha = GitHub.parseBranchHead json })
        | FetchEvidence(adapterId, sha), result ->
            let adapter = Evidence.registry |> List.find (fun a -> a.Id = adapterId)
            let outcome =
                match result with
                | GotText text -> Evidence.parse adapter text
                | Gone -> Evidence.Missing
                | Limited _ -> Evidence.FetchFailed "rate limited"
                | Offline -> Evidence.FetchFailed "network unavailable"
                | FailedWith r -> Evidence.FetchFailed r
                | _ -> Evidence.FetchFailed "unexpected response"
            let source : Evidence.Source = { Adapter = adapter; Sha = sha; Outcome = outcome }
            model
            |> withSnapshot (fun s ->
                { s with Evidence = (s.Evidence |> List.filter (fun e -> e.Adapter.Id <> adapterId)) @ [ source ] }
                |> setEndpoint key (match outcome with Evidence.FetchFailed r -> EndpointFailed r | _ -> EndpointOk))
        | (FetchPulls | FetchRuns | FetchDeployments | FetchHeadChecks _ | FetchDeploymentStatuses _ | RecheckHead | FetchCommits _), Gone ->
            model |> withSnapshot (setEndpoint key (EndpointEmpty "not available for this repository"))
        | _ -> fail ()

    let private onHttp (cid: string) (outcome: EffectOutcome) (model: Model) : Step =
        match model.Pending |> Map.tryFind cid with
        | None -> just model
        | Some pending ->
            let model = { model with Pending = model.Pending |> Map.remove cid }
            let current =
                pending.Generation = model.Generation
                && (match pending.Repo, model.Candidate, model.Selection with
                    | Some r, Some c, _ when RepoId.same r c -> true
                    | Some r, _, Some s when RepoId.same r s -> true
                    | _ -> false)
            if not current then
                just model // late response from a superseded refresh or repository
            else
                let model, fetched = resolve pending outcome model
                match fetched with
                | Superseded -> just model
                | _ ->
                    match pending.Request with
                    | FetchRepository -> onRepository pending fetched model
                    | request ->
                        let model = applyStage pending fetched model
                        let model =
                            match request, fetched with
                            | FetchCommits n, _ when n > 1 && not (isLoading model) -> model
                            | _, Limited retry -> { model with RetryAt = retry |> Option.orElse model.RetryAt }
                            | _ -> model
                        advance (stageOf request) model

    // ---- location and screens ----

    let private parseLocation (location: BrowserLocation) =
        let hash = location.Hash.TrimStart('#').TrimStart('/')
        let screenPart = hash.Split('?').[0]
        let query = location.Query.TrimStart('?')
        let repoParam =
            query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            |> Array.tryPick (fun kv ->
                match kv.Split('=', 2) with
                | [| "repo"; v |] -> Some(Uri.UnescapeDataString(v.Replace('+', ' ')))
                | _ -> None)
            |> Option.bind (fun v -> Repo.parse v |> Result.toOption)
        screenOfKey screenPart, repoParam

    let private screenFor (requested: Screen option) (model: Model) : Screen =
        match requested, model.Selection with
        | Some Picker, _ -> Picker
        | Some(Settings | About as s), _ -> s
        | Some s, Some _ -> s
        | None, Some _ -> screenOfKey model.Prefs.SelectedView |> Option.defaultValue Overview
        | Some _, None
        | None, None -> Picker

    let private rememberView (screen: Screen) (model: Model) : Step =
        match screen with
        | Overview | Activity | Verification | Replay when model.Prefs.SelectedView <> screenKey screen && model.SharedLink.IsNone ->
            savePrefs { model with Prefs = { model.Prefs with SelectedView = screenKey screen } }
        | _ -> just model

    let private onLocation (location: BrowserLocation) (model: Model) : Step =
        let requested, _ = parseLocation location
        let screen = screenFor requested model
        let model = { model with Screen = screen; Route = requested; Expanded = None; Notice = None }
        rememberView screen model

    // ---- preferences ----

    let private onPrefsLoaded (outcome: StorageOutcome) (model: Model) : Step =
        let model, notice, resave =
            match outcome with
            | StorageOutcome.Failure reason ->
                let why = match reason with StorageFailureReason.QuotaExceeded -> "storage is full" | StorageFailureReason.Unavailable -> "browser storage is unavailable or blocked"
                { model with Storage = SessionOnly why }, Some $"Preferences will not be remembered: {why}. Specula still works for this session.", false
            | StorageOutcome.Success value ->
                match Preferences.decode value with
                | Preferences.Loaded prefs -> { model with Prefs = prefs; Storage = StorageAvailable }, None, false
                | Preferences.Migrated(prefs, from) -> { model with Prefs = prefs; Storage = StorageAvailable }, Some $"Saved preferences were upgraded from the {from} format.", true
                | Preferences.NoPreferences -> { model with Storage = StorageAvailable }, None, false
                | Preferences.Discarded reason -> { model with Storage = StorageAvailable }, Some reason, true
        let shared = model.SharedLink
        let selection = shared |> Option.orElse model.Prefs.SelectedRepository
        let model = { model with Started = true; StorageNotice = notice; Selection = selection }
        let model = { model with Screen = screenFor model.Route model }
        let save = if resave then savePrefs else just
        save model
        |> andThen (fun m ->
            match m.Selection with
            | Some repo -> startRefresh repo false m
            | None -> just m)

    // ---- user commands ----

    let private observe (model: Model) : Step =
        match Repo.parse model.Draft with
        | Error e -> just { model with DraftError = Some(Repo.describe e) }
        | Ok repo when rateLimitBlocked model ->
            let until = model.RetryAt |> Option.map (Time.local model.Zone) |> Option.defaultValue "later"
            just { model with DraftError = Some $"GitHub's rate limit applies until {until}. {RepoId.text repo} was not checked." }
        | Ok repo -> startRefresh repo true { model with DraftError = None }

    let private competitionOf model = Model.competition model

    let private updateCompetition (f: Competition -> Competition) (message: string) (model: Model) : Step =
        match model.Selection with
        | None -> just model
        | Some repo ->
            let c = f (Preferences.competitionFor repo model.Prefs)
            savePrefs { model with Prefs = Preferences.withCompetition repo c model.Prefs; Competition = { model.Competition with Message = Some message } }

    let private saveCompetition (model: Model) : Step =
        let kickoffText = model.Competition.KickoffText.Trim()
        let durationText = model.Competition.DurationText.Trim()
        let current = competitionOf model
        let kickoff =
            if kickoffText = "" then Ok current.Kickoff
            else match Time.parseLocalInput model.Zone kickoffText with Some k -> Ok(Some k) | None -> Error "Kickoff must be a valid local date and time."
        let duration =
            if durationText = "" then Ok current.DurationMinutes
            else match Int32.TryParse durationText with | true, d when d > 0 && d <= 100000 -> Ok d | _ -> Error "Duration must be a whole number of minutes greater than zero."
        match kickoff, duration with
        | Ok k, Ok d ->
            updateCompetition (fun c -> { c with Kickoff = k; DurationMinutes = d }) "Competition clock saved for this repository." { model with Competition = { KickoffText = ""; DurationText = ""; Message = None } }
        | Error e, _ | _, Error e -> just { model with Competition = { model.Competition with Message = Some e } }

    let private setCheckpoint (milestone: string) (value: string) (model: Model) : Step =
        match Preferences.milestoneOfKey milestone with
        | None -> just model
        | Some kind ->
            let text = value.Trim()
            if text = "" then
                updateCompetition (fun c -> { c with Checkpoints = c.Checkpoints |> List.filter (fun cp -> cp.Milestone <> kind) }) "Checkpoint removed." model
            else
                match Int32.TryParse text with
                | true, offset when offset >= 0 && offset <= 100000 ->
                    updateCompetition
                        (fun c -> { c with Checkpoints = ({ Milestone = kind; OffsetMinutes = offset } :: (c.Checkpoints |> List.filter (fun cp -> cp.Milestone <> kind))) |> List.sortBy (fun cp -> cp.OffsetMinutes) })
                        "Checkpoint saved."
                        model
                | _ -> just { model with Competition = { model.Competition with Message = Some "Checkpoint offsets are whole minutes after kickoff." } }

    let private useRecordedKickoff (model: Model) : Step =
        match currentSnapshot model |> Option.bind (fun s -> Evidence.kickoff s.Evidence) with
        | None -> just { model with Competition = { model.Competition with Message = Some "No supported kickoff record was found in this repository's evidence." } }
        | Some k ->
            let duration =
                match k.LimitMinutes, k.Deadline with
                | Some m, _ when m > 0L -> int m
                | _, Some d when d > k.T0 -> int (d - k.T0).TotalMinutes
                | _ -> (competitionOf model).DurationMinutes
            updateCompetition (fun c -> { c with Kickoff = Some k.T0; DurationMinutes = duration }) $"Kickoff set from evidence/kickoff/kickoff.json ({Time.utc k.T0})." model

    let private replayFromCompetition (model: Model) : Step =
        match Metrics.clock model.Now (competitionOf model) with
        | None -> just { model with Replay = { model.Replay with Error = Some "Configure a kickoff in Settings to use the competition window." } }
        | Some clock ->
            just
                { model with
                    Replay =
                        { model.Replay with
                            StartText = Time.toLocalInput model.Zone clock.Kickoff
                            EndText = Time.toLocalInput model.Zone clock.Deadline
                            Error = None } }

    let private forget (model: Model) : Step =
        cancelHttp model
        |> andThen (fun m ->
            let m =
                { m with
                    Selection = None
                    Snapshot = None
                    Candidate = None
                    SharedLink = None
                    Fetch = Idle
                    Generation = m.Generation + 1
                    LastSuccess = None
                    LastAttempt = None
                    Prefs = { m.Prefs with SelectedRepository = None } }
            savePrefs m |> andThen (navigate Picker))

    let update (msg: Msg) (model: Model) : Step =
        match msg with
        | Init location ->
            let requested, shared = parseLocation location
            { model with Path = location.Path; SharedLink = shared; Route = requested; Screen = Picker }
            |> register { Request = LoadPreferences; Repo = None; Generation = 0; Url = None } (fun cid ->
                EffectRequest.Storage(StorageEffectRequest.Get(cid, Preferences.storageKey)))
        | LocationChanged location -> onLocation location model
        | DraftChanged text -> just { model with Draft = text; DraftError = None }
        | Observe -> observe model
        | ChooseRecent key ->
            match model.Prefs.RecentRepositories |> List.tryFind (fun r -> RepoId.key r = key) with
            | Some repo -> observe { model with Draft = RepoId.text repo }
            | None -> just model
        | Refresh -> refresh model
        | LoadMoreCommits ->
            match model.Selection, currentSnapshot model with
            | Some repo, Some s when s.MoreCommits && not (isLoading model) ->
                let alreadyLoading = httpPending model |> Map.exists (fun _ p -> match p.Request with FetchCommits n -> n > 1 | _ -> false)
                if alreadyLoading then just model else fetch repo (FetchCommits(s.CommitPage + 1)) model
            | _ -> just model
        | SetFilter key -> just { model with Filter = Events.filterOfKey key |> Option.defaultValue model.Filter }
        | ToggleEvent id -> just { model with Expanded = (if model.Expanded = Some id then None else Some id) }
        | Forget -> forget model
        | ReplayStart text -> just { model with Replay = { model.Replay with StartText = text; Error = None } }
        | ReplayEnd text -> just { model with Replay = { model.Replay with EndText = text; Error = None } }
        | ReplayFilter key -> just { model with Replay = { model.Replay with Filter = Events.filterOfKey key |> Option.defaultValue model.Replay.Filter } }
        | ReplayUseCompetition -> replayFromCompetition model
        | TogglePresentation -> just { model with Replay = { model.Replay with Presentation = not model.Replay.Presentation } }
        | KickoffChanged text -> just { model with Competition = { model.Competition with KickoffText = text; Message = None } }
        | DurationChanged text -> just { model with Competition = { model.Competition with DurationText = text; Message = None } }
        | CheckpointChanged(milestone, value) -> setCheckpoint milestone value model
        | SaveCompetition -> saveCompetition model
        | UseRecordedKickoff -> useRecordedKickoff model
        | ClearCompetition -> updateCompetition (fun _ -> Preferences.indyInitPreset) "Competition clock reset to the Indy Init preset (no kickoff)." model
        | SetTheme key -> savePrefs { model with Prefs = { model.Prefs with Theme = Preferences.themeOfKey key } }
        | ClearAllSettings ->
            cancelHttp model
            |> andThen (fun m ->
                let m = { m with Prefs = Preferences.empty; Selection = None; Snapshot = None; Fetch = Idle; Generation = m.Generation + 1; LastSuccess = None; SharedLink = None }
                (match m.Storage with
                 | SessionOnly _ -> just m
                 | _ ->
                     m
                     |> register { Request = SavePreferences; Repo = None; Generation = m.Generation; Url = None } (fun cid ->
                         EffectRequest.Storage(StorageEffectRequest.Remove(cid, Preferences.storageKey))))
                |> andThen (navigate Picker))
        | SaveSharedAsDefault ->
            match model.Selection with
            | Some repo -> savePrefs { model with SharedLink = None; Prefs = { model.Prefs with SelectedRepository = Some repo } |> Preferences.withRecent repo }
            | None -> just model
        | DismissNotice -> just { model with Notice = None; StorageNotice = None }
        | HttpDone(cid, outcome) -> onHttp cid outcome model
        | StorageDone(cid, outcome) ->
            match model.Pending |> Map.tryFind cid with
            | Some { Request = LoadPreferences } -> onPrefsLoaded outcome { model with Pending = model.Pending |> Map.remove cid }
            | Some _ ->
                let model = { model with Pending = model.Pending |> Map.remove cid }
                match outcome with
                | StorageOutcome.Failure StorageFailureReason.QuotaExceeded ->
                    just { model with Storage = SessionOnly "storage is full"; StorageNotice = Some "Preferences could not be saved: browser storage is full. Specula keeps working for this session." }
                | StorageOutcome.Failure StorageFailureReason.Unavailable ->
                    just { model with Storage = SessionOnly "browser storage is unavailable"; StorageNotice = Some "Preferences could not be saved: browser storage is unavailable. Specula keeps working for this session." }
                | StorageOutcome.Success _ -> just model
            | None -> just model
        | Ignored -> just model

    /// Map a Limen semantic event to a message. Unknown names are wiring bugs.
    let ofEvent (event: SemanticEvent) : Msg =
        let value = event.Value |> Option.defaultValue ""
        let key = event.Key |> Option.defaultValue ""
        match event.Name with
        | "draft" -> DraftChanged value
        | "observe" -> Observe
        | "recent" -> ChooseRecent key
        | "refresh" | "retry" -> Refresh
        | "more" -> LoadMoreCommits
        | "filter" -> SetFilter key
        | "toggle" -> ToggleEvent key
        | "forget" -> Forget
        | "replayStart" -> ReplayStart value
        | "replayEnd" -> ReplayEnd value
        | "replayFilter" -> ReplayFilter key
        | "replayCompetition" -> ReplayUseCompetition
        | "presentation" -> TogglePresentation
        | "kickoff" -> KickoffChanged value
        | "duration" -> DurationChanged value
        | "checkpoint" -> CheckpointChanged(key, value)
        | "saveCompetition" -> SaveCompetition
        | "recordedKickoff" -> UseRecordedKickoff
        | "clearCompetition" -> ClearCompetition
        | "theme" -> SetTheme value
        | "clearAll" -> ClearAllSettings
        | "saveShared" -> SaveSharedAsDefault
        | "dismiss" -> DismissNotice
        | other -> failwith $"Unrecognized event: {other}"
