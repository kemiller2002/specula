namespace Specula.Engine

open System
open System.Text.Json

/// GitHub public REST adapter: URL construction and response normalization.
/// Never emits Echelon claims (CAPABILITIES "Adapter boundaries").
module GitHub =

    let pageSize = 30
    let maxCommitPages = 5

    let urls (repo: RepoId) =
        let api = Repo.apiRepo repo
        {| Repository = api
           Commits = fun (branch: string) (page: int) -> $"{api}/commits?sha={Uri.EscapeDataString branch}&per_page={pageSize}&page={page}"
           Pulls = $"{api}/pulls?state=all&sort=updated&direction=desc&per_page={pageSize}"
           Runs = fun (branch: string) -> $"{api}/actions/runs?branch={Uri.EscapeDataString branch}&per_page=50"
           CheckRuns = fun (sha: string) -> $"{api}/commits/{sha}/check-runs?per_page=100"
           Deployments = $"{api}/deployments?per_page={pageSize}"
           DeploymentStatuses = fun (id: int64) -> $"{api}/deployments/{id}/statuses?per_page=10"
           Branch = fun (branch: string) -> $"{api}/branches/{Uri.EscapeDataString branch}"
           Contents = fun (path: string) (sha: string) -> $"{api}/contents/{path}?ref={sha}" |}

    let responseHeaders =
        [ "etag"; "link"; "retry-after"; "x-ratelimit-limit"; "x-ratelimit-remaining"; "x-ratelimit-reset"; "x-ratelimit-used" ]

    let private safeUrl fallback url =
        url |> Option.bind Repo.safeGitHubUrl |> Option.defaultValue fallback

    let parseRepository (requested: RepoId) (json: JsonElement) : Repository option =
        let owner = Json.strAt [ "owner"; "login" ] json
        let name = Json.str "name" json
        match owner, name, Json.str "default_branch" json with
        | Some owner, Some name, branch ->
            let id = { Owner = owner; Name = name }
            Some
                { Id = id
                  DefaultBranch = branch |> Option.defaultValue "main"
                  HtmlUrl = Json.str "html_url" json |> safeUrl (Repo.repoUrl id)
                  Description = Json.str "description" json
                  IsPrivate = Json.bool "private" json |> Option.defaultValue false
                  IsArchived = Json.bool "archived" json |> Option.defaultValue false
                  CreatedAt = Json.time "created_at" json
                  PushedAt = Json.time "pushed_at" json }
        | _ -> ignore requested; None

    let firstLine (text: string) =
        let line = text.Split('\n').[0].Trim()
        if line.Length > 200 then line.Substring(0, 199) + "…" else line

    let parseCommit (repo: RepoId) (json: JsonElement) : Commit option =
        Json.str "sha" json
        |> Option.map (fun sha ->
            { Sha = sha
              Message = Json.strAt [ "commit"; "message" ] json |> Option.defaultValue ""
              AuthorName = Json.strAt [ "commit"; "author"; "name" ] json
              AuthorLogin = Json.strAt [ "author"; "login" ] json
              Date = Json.timeAt [ "commit"; "committer"; "date" ] json |> Option.orElse (Json.timeAt [ "commit"; "author"; "date" ] json)
              HtmlUrl = Json.str "html_url" json |> safeUrl $"{Repo.repoUrl repo}/commit/{sha}" })

    let parseCommits (repo: RepoId) (json: JsonElement) : Commit list =
        Json.asArray json |> List.choose (parseCommit repo)

    let parsePull (repo: RepoId) (json: JsonElement) : PullRequest option =
        Json.int64 "number" json
        |> Option.map (fun number ->
            let merged = Json.time "merged_at" json
            let state =
                match Json.str "state" json, merged with
                | _, Some _ -> PullMerged
                | Some "open", _ -> PullOpen
                | _ -> PullClosed
            { Number = number
              Title = Json.str "title" json |> Option.defaultValue $"#{number}"
              State = state
              Draft = Json.bool "draft" json |> Option.defaultValue false
              Author = Json.strAt [ "user"; "login" ] json
              CreatedAt = Json.time "created_at" json
              UpdatedAt = Json.time "updated_at" json
              ClosedAt = Json.time "closed_at" json
              MergedAt = merged
              HeadSha = Json.strAt [ "head"; "sha" ] json
              MergeSha = Json.str "merge_commit_sha" json
              HtmlUrl = Json.str "html_url" json |> safeUrl $"{Repo.repoUrl repo}/pull/{number}" })

    let parsePulls (repo: RepoId) (json: JsonElement) : PullRequest list =
        Json.asArray json |> List.choose (parsePull repo)

    /// Maps GitHub status + conclusion to the closed Outcome set.
    let outcomeOf (status: string option) (conclusion: string option) : Outcome =
        match status, conclusion with
        | Some "completed", Some c ->
            match c with
            | "success" -> Passed
            | "failure" | "timed_out" | "startup_failure" | "action_required" -> Failed
            | "cancelled" -> Cancelled
            | "skipped" -> Skipped
            | "neutral" -> Neutral
            | _ -> UnknownOutcome
        | Some "completed", None -> UnknownOutcome
        | Some ("queued" | "waiting" | "requested" | "pending"), _ -> Queued
        | Some "in_progress", _ -> Running
        | _ -> UnknownOutcome

    let parseRun (repo: RepoId) (json: JsonElement) : WorkflowRun option =
        match Json.int64 "id" json, Json.str "head_sha" json with
        | Some id, Some sha ->
            Some
                { Id = id
                  Name = Json.str "name" json |> Option.orElse (Json.str "display_title" json) |> Option.defaultValue $"Run {id}"
                  HeadSha = sha
                  HeadBranch = Json.str "head_branch" json
                  Event = Json.str "event" json
                  Outcome = outcomeOf (Json.str "status" json) (Json.str "conclusion" json)
                  RunAttempt = Json.int64 "run_attempt" json |> Option.defaultValue 1L
                  CreatedAt = Json.time "created_at" json
                  UpdatedAt = Json.time "updated_at" json
                  HtmlUrl = Json.str "html_url" json |> safeUrl $"{Repo.repoUrl repo}/actions/runs/{id}" }
        | _ -> None

    let parseRuns (repo: RepoId) (json: JsonElement) : WorkflowRun list * int64 option =
        Json.array "workflow_runs" json |> List.choose (parseRun repo), Json.int64 "total_count" json

    let parseCheckRun (repo: RepoId) (json: JsonElement) : CheckRun option =
        match Json.int64 "id" json, Json.str "head_sha" json with
        | Some id, Some sha ->
            Some
                { Id = id
                  Name = Json.str "name" json |> Option.defaultValue $"Check {id}"
                  HeadSha = sha
                  Outcome = outcomeOf (Json.str "status" json) (Json.str "conclusion" json)
                  App = Json.strAt [ "app"; "name" ] json
                  StartedAt = Json.time "started_at" json
                  CompletedAt = Json.time "completed_at" json
                  HtmlUrl = Json.str "html_url" json |> safeUrl $"{Repo.repoUrl repo}/commit/{sha}/checks" }
        | _ -> None

    let parseCheckRuns (repo: RepoId) (json: JsonElement) : CheckRun list * int64 option =
        Json.array "check_runs" json |> List.choose (parseCheckRun repo), Json.int64 "total_count" json

    let parseDeployment (json: JsonElement) : Deployment option =
        match Json.int64 "id" json, Json.str "sha" json with
        | Some id, Some sha ->
            Some
                { Id = id
                  Sha = sha
                  Ref = Json.str "ref" json
                  Environment = Json.str "environment" json |> Option.defaultValue "unknown"
                  Creator = Json.strAt [ "creator"; "login" ] json
                  CreatedAt = Json.time "created_at" json
                  StatusesUrl = Json.str "statuses_url" json }
        | _ -> None

    let parseDeployments (json: JsonElement) : Deployment list =
        Json.asArray json |> List.choose parseDeployment

    let deploymentStateOf (state: string) =
        match state with
        | "success" -> DeploySuccess
        | "failure" -> DeployFailure
        | "error" -> DeployError
        | "inactive" -> DeployInactive
        | "in_progress" -> DeployInProgress
        | "queued" -> DeployQueued
        | "pending" -> DeployPending
        | other -> DeployUnknown other

    let parseDeploymentStatus (json: JsonElement) : DeploymentStatus option =
        Json.int64 "id" json
        |> Option.map (fun id ->
            { Id = id
              State = Json.str "state" json |> Option.map deploymentStateOf |> Option.defaultValue (DeployUnknown "missing")
              CreatedAt = Json.time "created_at" json
              EnvironmentUrl = Json.str "environment_url" json |> Option.filter (fun s -> s <> "")
              LogUrl = Json.str "log_url" json |> Option.filter (fun s -> s <> "") })

    let parseDeploymentStatuses (json: JsonElement) : DeploymentStatus list =
        Json.asArray json |> List.choose parseDeploymentStatus

    let parseBranchHead (json: JsonElement) : string option =
        Json.strAt [ "commit"; "sha" ] json

    /// RFC 8288 Link header: does a rel="next" page exist?
    let hasNextPage (link: string option) : bool =
        link |> Option.exists (fun l -> l.Split(',') |> Array.exists (fun part -> part.Contains "rel=\"next\""))

    type RateLimit =
        { Limit: int64 option
          Remaining: int64 option
          Reset: DateTimeOffset option }

    let private header (name: string) (headers: Map<string, string>) =
        headers |> Map.tryFindKey (fun k _ -> String.Equals(k, name, StringComparison.OrdinalIgnoreCase)) |> Option.map (fun k -> headers.[k])

    let headerValue = header

    let rateLimitOf (headers: Map<string, string>) : RateLimit option =
        let num name = header name headers |> Option.bind (fun v -> match Int64.TryParse v with | true, n -> Some n | _ -> None)
        match num "x-ratelimit-limit", num "x-ratelimit-remaining" with
        | None, None -> None
        | limit, remaining ->
            Some
                { Limit = limit
                  Remaining = remaining
                  Reset = num "x-ratelimit-reset" |> Option.map DateTimeOffset.FromUnixTimeSeconds }

    /// Classification of an HTTP response from the public API.
    [<NoComparison>]
    type ApiResponse =
        | Body of JsonElement
        | NotModified
        | NotFound
        | Forbidden of rateLimited: bool * retryAt: DateTimeOffset option * message: string
        | Conflict of message: string
        | ServerError of status: int64
        | Malformed of status: int64

    let private messageOf (body: JsonElement option) =
        body |> Option.bind (Json.str "message") |> Option.defaultValue ""

    let classify (now: DateTimeOffset) (status: int64) (body: JsonElement option) (headers: Map<string, string>) : ApiResponse =
        let limit = rateLimitOf headers
        let retryAfter =
            header "retry-after" headers
            |> Option.bind (fun v -> match Int64.TryParse v with | true, s -> Some(now.AddSeconds(float s)) | _ -> None)
        let resetAt = limit |> Option.bind (fun l -> l.Reset)
        match status with
        | s when s >= 200L && s < 300L ->
            match body with
            | Some json -> Body json
            | None -> Malformed s
        | 304L -> NotModified
        | 404L | 451L -> NotFound
        | 403L | 429L ->
            let exhausted = limit |> Option.bind (fun l -> l.Remaining) = Some 0L
            let message = messageOf body
            let rateLimited = status = 429L || exhausted || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            Forbidden(rateLimited, retryAfter |> Option.orElse resetAt, message)
        | 409L -> Conflict(messageOf body)
        | s when s >= 500L -> ServerError s
        | s -> Malformed s
