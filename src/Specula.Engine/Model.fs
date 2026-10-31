namespace Specula.Engine

open System
open Specula.Engine.Preferences

/// Application state. Exactly one value of this type exists at a time; the
/// DOM is a projection of it (Limen mental model).
module Model =

    type Screen =
        | Picker
        | Overview
        | Activity
        | Verification
        | Replay
        | Settings
        | About

    let screenKey screen =
        match screen with
        | Picker -> "select"
        | Overview -> "overview"
        | Activity -> "activity"
        | Verification -> "verification"
        | Replay -> "replay"
        | Settings -> "settings"
        | About -> "about"

    let screenOfKey key =
        match key with
        | "select" -> Some Picker
        | "overview" -> Some Overview
        | "activity" -> Some Activity
        | "verification" -> Some Verification
        | "replay" -> Some Replay
        | "settings" -> Some Settings
        | "about" -> Some About
        | _ -> None

    let screenTitle screen =
        match screen with
        | Picker -> "Choose a repository"
        | Overview -> "Overview"
        | Activity -> "Activity"
        | Verification -> "Verification"
        | Replay -> "Replay"
        | Settings -> "Settings"
        | About -> "About Specula"

    /// What an in-flight effect was asked for. Responses are matched to the
    /// request by correlation id, then accepted only when their repository
    /// and generation still match the current refresh (SPEC-D-010).
    type Request =
        | LoadPreferences
        | SavePreferences
        | FetchRepository
        | FetchCommits of page: int
        | FetchPulls
        | FetchRuns
        | FetchDeployments
        | FetchHeadChecks of sha: string
        | FetchDeploymentStatuses of deploymentId: int64
        | FetchEvidence of adapterId: string * sha: string
        | RecheckHead

    type Pending =
        { Request: Request
          Repo: RepoId option
          Generation: int
          Url: string option }

    type EndpointStatus =
        | EndpointOk
        | EndpointEmpty of reason: string
        | EndpointFailed of reason: string
        | EndpointRateLimited

    type Snapshot =
        { Repo: RepoId
          Repository: Repository option
          Commits: Commit list
          CommitPage: int
          MoreCommits: bool
          Pulls: PullRequest list
          Runs: WorkflowRun list
          RunsTotal: int64 option
          HeadSha: string option
          HeadChecks: CheckRun list
          ChecksTotal: int64 option
          Deployments: Deployment list
          DeploymentStatuses: Map<int64, DeploymentStatus list>
          Evidence: Evidence.Source list
          Endpoints: Map<string, EndpointStatus>
          RecheckSha: string option
          ObservedAt: DateTimeOffset option }

    let emptySnapshot (repo: RepoId) =
        { Repo = repo
          Repository = None
          Commits = []
          CommitPage = 0
          MoreCommits = false
          Pulls = []
          Runs = []
          RunsTotal = None
          HeadSha = None
          HeadChecks = []
          ChecksTotal = None
          Deployments = []
          DeploymentStatuses = Map.empty
          Evidence = []
          Endpoints = Map.empty
          RecheckSha = None
          ObservedAt = None }

    /// SCREENS "Refresh and persistence state machine".
    type FetchState =
        | Idle
        | Loading
        | Fresh of observedAt: DateTimeOffset
        | Partial of observedAt: DateTimeOffset * reason: string
        | Stale of lastSuccess: DateTimeOffset option * reason: string
        | RateLimited of retryAfter: DateTimeOffset option
        | FetchError of reason: string

    type StorageHealth =
        | StorageUnknown
        | StorageAvailable
        | SessionOnly of reason: string

    type ReplayState =
        { StartText: string
          EndText: string
          Filter: Events.Filter
          Presentation: bool
          Error: string option }

    type CompetitionDraft =
        { KickoffText: string
          DurationText: string
          Message: string option }

    [<NoComparison>]
    type Model =
        { Now: DateTimeOffset
          Zone: TimeZoneInfo
          Path: string
          Started: bool
          Prefs: Prefs
          Storage: StorageHealth
          StorageNotice: string option
          Selection: RepoId option
          SharedLink: RepoId option
          Screen: Screen
          Route: Screen option
          Filter: Events.Filter
          Expanded: string option
          Draft: string
          DraftError: string option
          Candidate: RepoId option
          Generation: int
          Sequence: int
          Pending: Map<string, Pending>
          Snapshot: Snapshot option
          Fetch: FetchState
          LastAttempt: DateTimeOffset option
          LastSuccess: DateTimeOffset option
          RateLimit: GitHub.RateLimit option
          RetryAt: DateTimeOffset option
          Etags: Map<string, string * string>
          RequestCount: int
          Notice: string option
          Replay: ReplayState
          Competition: CompetitionDraft }

    let initial (now: DateTimeOffset) (zone: TimeZoneInfo) : Model =
        { Now = now
          Zone = zone
          Path = "/"
          Started = false
          Prefs = Preferences.empty
          Storage = StorageUnknown
          StorageNotice = None
          Selection = None
          SharedLink = None
          Screen = Picker
          Route = None
          Filter = Events.AllEvents
          Expanded = None
          Draft = ""
          DraftError = None
          Candidate = None
          Generation = 0
          Sequence = 0
          Pending = Map.empty
          Snapshot = None
          Fetch = Idle
          LastAttempt = None
          LastSuccess = None
          RateLimit = None
          RetryAt = None
          Etags = Map.empty
          RequestCount = 0
          Notice = None
          Replay = { StartText = ""; EndText = ""; Filter = Events.AllEvents; Presentation = false; Error = None }
          Competition = { KickoffText = ""; DurationText = ""; Message = None } }

    /// The snapshot only if it belongs to the selected repository: data from
    /// a previous repository can never be projected (acceptance scenario 3).
    let currentSnapshot (model: Model) : Snapshot option =
        match model.Selection, model.Snapshot with
        | Some repo, Some snapshot when RepoId.same repo snapshot.Repo -> Some snapshot
        | _ -> None

    let isLoading (model: Model) = model.Fetch = Loading

    let competition (model: Model) : Competition =
        match model.Selection with
        | Some repo -> Preferences.competitionFor repo model.Prefs
        | None -> Preferences.indyInitPreset

    /// All source events in the current snapshot, deduplicated and ordered.
    let events (snapshot: Snapshot) : SourceEvent list =
        [ snapshot.Commits |> List.choose Events.ofCommit
          snapshot.Pulls |> List.collect Events.ofPull
          snapshot.Runs |> List.choose Events.ofRun
          snapshot.HeadChecks |> List.choose Events.ofCheck
          snapshot.Deployments |> List.choose (Events.ofDeployment snapshot.Repo snapshot.DeploymentStatuses) ]
        |> List.concat
        |> Events.normalize
