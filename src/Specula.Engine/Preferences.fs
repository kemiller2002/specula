namespace Specula.Engine

open System
open System.IO
open System.Text
open System.Text.Json

/// Versioned, non-sensitive browser preferences (SPEC-D-007, SPEC-D-009,
/// SPEC-G-018). Never contains credentials or downloaded GitHub evidence.
module Preferences =

    let storageKey = "specula.preferences"
    let currentVersion = 1L
    let maxRecent = 8

    type MilestoneKind =
        | RepositoryCreated
        | FirstCiRun
        | FirstPassingCi
        | FirstDeployment
        | RequirementsEvidence
        | AcceptanceVerdict

    let milestoneKinds =
        [ RepositoryCreated; FirstCiRun; FirstPassingCi; FirstDeployment; RequirementsEvidence; AcceptanceVerdict ]

    let milestoneKey kind =
        match kind with
        | RepositoryCreated -> "repository-created"
        | FirstCiRun -> "first-ci-run"
        | FirstPassingCi -> "first-passing-ci"
        | FirstDeployment -> "first-deployment"
        | RequirementsEvidence -> "requirements-evidence"
        | AcceptanceVerdict -> "acceptance"

    let milestoneOfKey key = milestoneKinds |> List.tryFind (fun k -> milestoneKey k = key)

    let milestoneLabel kind =
        match kind with
        | RepositoryCreated -> "Repository created"
        | FirstCiRun -> "First CI run"
        | FirstPassingCi -> "First passing CI"
        | FirstDeployment -> "First evidenced deployment"
        | RequirementsEvidence -> "Requirements evidence"
        | AcceptanceVerdict -> "Acceptance verdict"

    type Checkpoint = { Milestone: MilestoneKind; OffsetMinutes: int }

    /// Competition preset, stored per repository (GAP-CLOSURE open decision 2).
    type Competition =
        { Kickoff: DateTimeOffset option
          DurationMinutes: int
          Checkpoints: Checkpoint list }

    /// Indy Init rehearsals ran a 420-minute window. Checkpoint offsets are
    /// planning configuration, never observations.
    let indyInitPreset =
        { Kickoff = None
          DurationMinutes = 420
          Checkpoints =
            [ { Milestone = FirstCiRun; OffsetMinutes = 30 }
              { Milestone = FirstPassingCi; OffsetMinutes = 60 }
              { Milestone = FirstDeployment; OffsetMinutes = 120 }
              { Milestone = RequirementsEvidence; OffsetMinutes = 300 }
              { Milestone = AcceptanceVerdict; OffsetMinutes = 420 } ] }

    type Theme =
        | SystemTheme
        | LightTheme
        | DarkTheme

    let themeKey theme =
        match theme with
        | SystemTheme -> "system"
        | LightTheme -> "light"
        | DarkTheme -> "dark"

    let themeOfKey key =
        match key with
        | "light" -> LightTheme
        | "dark" -> DarkTheme
        | _ -> SystemTheme

    type Prefs =
        { SelectedRepository: RepoId option
          RecentRepositories: RepoId list
          SelectedView: string
          AutoRefreshSeconds: int
          Theme: Theme
          Competitions: Map<string, Competition> }

    let empty =
        { SelectedRepository = None
          RecentRepositories = []
          SelectedView = "overview"
          AutoRefreshSeconds = 0
          Theme = LightTheme
          Competitions = Map.empty }

    let knownViews = [ "overview"; "activity"; "verification"; "replay" ]

    let withRecent (repo: RepoId) (prefs: Prefs) =
        { prefs with
            RecentRepositories =
                repo :: (prefs.RecentRepositories |> List.filter (fun r -> not (RepoId.same r repo)))
                |> List.truncate maxRecent }

    let competitionFor (repo: RepoId) (prefs: Prefs) =
        prefs.Competitions |> Map.tryFind (RepoId.key repo) |> Option.defaultValue indyInitPreset

    let withCompetition (repo: RepoId) (competition: Competition) (prefs: Prefs) =
        { prefs with Competitions = prefs.Competitions |> Map.add (RepoId.key repo) competition }

    // ---- decoding (defensive) ----

    type LoadResult =
        | Loaded of Prefs
        | Migrated of Prefs * fromVersion: string
        | NoPreferences
        | Discarded of reason: string

    let private repoOf (text: string) = Repo.parse text |> Result.toOption

    let private decodeCheckpoint json =
        match Json.str "milestone" json |> Option.bind milestoneOfKey, Json.int64 "offsetMinutes" json with
        | Some m, Some o when o >= 0L && o <= 100000L -> Some { Milestone = m; OffsetMinutes = int o }
        | _ -> None

    let private decodeCompetition json =
        { Kickoff = Json.time "kickoff" json
          DurationMinutes =
            Json.int64 "durationMinutes" json
            |> Option.filter (fun d -> d > 0L && d <= 100000L)
            |> Option.map int
            |> Option.defaultValue indyInitPreset.DurationMinutes
          Checkpoints = Json.array "checkpoints" json |> List.choose decodeCheckpoint }

    let private decodeCompetitions json =
        match Json.prop "competitions" json with
        | Some c when c.ValueKind = JsonValueKind.Object ->
            c.EnumerateObject()
            |> Seq.choose (fun p -> repoOf p.Name |> Option.map (fun r -> RepoId.key r, decodeCompetition p.Value))
            |> Seq.truncate 50
            |> Map.ofSeq
        | _ -> Map.empty

    let private decodeV1 json =
        { SelectedRepository = Json.str "selectedRepository" json |> Option.bind repoOf
          RecentRepositories =
            Json.array "recentRepositories" json
            |> List.choose (Json.asString >> Option.bind repoOf)
            |> List.distinctBy RepoId.key
            |> List.truncate maxRecent
          SelectedView = Json.str "selectedView" json |> Option.filter (fun v -> List.contains v knownViews) |> Option.defaultValue "overview"
          AutoRefreshSeconds = 0
          Theme = Json.str "theme" json |> Option.map themeOfKey |> Option.defaultValue LightTheme
          Competitions = decodeCompetitions json }

    let decode (stored: string option) : LoadResult =
        match stored with
        | None -> NoPreferences
        | Some text when text.Length > 64 * 1024 -> Discarded "Saved preferences were unexpectedly large and were ignored."
        | Some text ->
            match Json.tryParse text with
            | None -> Discarded "Saved preferences were corrupted and were reset."
            | Some json when json.ValueKind <> JsonValueKind.Object -> Discarded "Saved preferences had an unexpected shape and were reset."
            | Some json ->
                match Json.int64 "version" json, Json.prop "version" json with
                | Some 1L, _ -> Loaded(decodeV1 json)
                | None, None when Json.str "selectedRepository" json |> Option.isSome ->
                    // Pre-release unversioned shape: { selectedRepository, recentRepositories }.
                    Migrated(decodeV1 json, "unversioned")
                | Some v, _ when v > currentVersion -> Discarded $"Saved preferences use a newer format (version {v}) and were ignored."
                | _ -> Discarded "Saved preferences used an unknown format and were reset."

    // ---- encoding ----

    let encode (prefs: Prefs) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteNumber("version", currentVersion)
        match prefs.SelectedRepository with
        | Some r -> writer.WriteString("selectedRepository", RepoId.text r)
        | None -> writer.WriteNull("selectedRepository")
        writer.WriteStartArray("recentRepositories")
        prefs.RecentRepositories |> List.iter (fun r -> writer.WriteStringValue(RepoId.text r))
        writer.WriteEndArray()
        writer.WriteString("selectedView", prefs.SelectedView)
        writer.WriteNumber("autoRefreshSeconds", prefs.AutoRefreshSeconds)
        writer.WriteString("theme", themeKey prefs.Theme)
        writer.WriteStartObject("competitions")
        prefs.Competitions
        |> Map.iter (fun key c ->
            writer.WriteStartObject(key)
            match c.Kickoff with
            | Some k -> writer.WriteString("kickoff", Time.iso k)
            | None -> writer.WriteNull("kickoff")
            writer.WriteNumber("durationMinutes", c.DurationMinutes)
            writer.WriteStartArray("checkpoints")
            c.Checkpoints
            |> List.iter (fun cp ->
                writer.WriteStartObject()
                writer.WriteString("milestone", milestoneKey cp.Milestone)
                writer.WriteNumber("offsetMinutes", cp.OffsetMinutes)
                writer.WriteEndObject())
            writer.WriteEndArray()
            writer.WriteEndObject())
        writer.WriteEndObject()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
