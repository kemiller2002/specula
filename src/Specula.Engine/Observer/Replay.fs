namespace Specula.Engine.Observer

open System
open Specula.Engine.Observer.Types

/// Time-correct visibility. `snapshotAt` is the only way the rules receive
/// historical data, and it drops every event the observer could not have
/// seen at `at`: later commits, CI runs not yet created, results not yet
/// reported. Sealed artifacts contribute their path only.
module Replay =

    let minuteOf (clock: Clock) (at: DateTimeOffset) = int (floor (at - clock.T0).TotalMinutes)

    let atMinute (clock: Clock) (minute: int) = clock.T0.AddMinutes(float minute)

    /// When the observer could first see a commit, or None if it never could.
    let commitVisibleAt (mode: Visibility) (commit: Commit) : DateTimeOffset option =
        match mode with
        | Visibility.Local -> Some commit.CommittedAt
        | Visibility.Remote -> commit.RemoteVisibleAt

    let isCommitVisible mode (at: DateTimeOffset) commit =
        commitVisibleAt mode commit |> Option.exists (fun t -> t <= at)

    let runView (at: DateTimeOffset) (run: CiRun) : RunView option =
        if run.CreatedAt > at then None
        else
            match run.CompletedAt with
            | Some done' when done' <= at -> Some { Run = run; Completed = true }
            | _ ->
                // Still running at `at`: its outcome and log are not yet known.
                Some
                    { Run = { run with Conclusion = "in_progress"; FailedStep = None; LogExcerpt = []; LogCollected = false; CompletedAt = None }
                      Completed = false }

    let snapshotAt (mode: Visibility) (at: DateTimeOffset) (timeline: Timeline) : Snapshot =
        let commits = timeline.Commits |> List.filter (isCommitVisible mode at)
        let visibleShas = commits |> List.map (fun c -> c.Sha) |> Set.ofList
        { At = at
          Mode = mode
          Clock = timeline.Clock
          Commits = commits |> List.sortBy (fun c -> c.CommittedAt)
          TestDataWrites = timeline.TestDataWrites |> List.filter (fun w -> w.At <= at)
          Runs = timeline.Runs |> List.choose (runView at)
          SealedPaths =
            timeline.Sealed
            |> List.filter (fun s -> visibleShas.Contains s.Commit)
            |> List.map (fun s -> s.Path)
          Unobservable = timeline.Unobservable }

    /// The timeline truncated to what existed at `at`. Used by tests to prove
    /// that findings at `at` do not depend on anything later.
    let truncate (at: DateTimeOffset) (timeline: Timeline) : Timeline =
        { timeline with
            Commits = timeline.Commits |> List.filter (fun c -> c.CommittedAt <= at)
            TestDataWrites = timeline.TestDataWrites |> List.filter (fun w -> w.At <= at)
            Runs =
                timeline.Runs
                |> List.filter (fun r -> r.CreatedAt <= at)
                |> List.map (fun r ->
                    match r.CompletedAt with
                    | Some d when d <= at -> r
                    | _ -> { r with CompletedAt = None; Conclusion = "in_progress"; FailedStep = None; LogExcerpt = []; LogCollected = false })
            Sealed = timeline.Sealed |> List.filter (fun s -> timeline.Commits |> List.exists (fun c -> c.Sha = s.Commit && c.CommittedAt <= at)) }

    let builderCommits (snapshot: Snapshot) =
        snapshot.Commits |> List.filter (fun c -> c.Role = CommitRole.Builder || c.Role = CommitRole.Merge || c.Role = CommitRole.Bootstrap)

    /// The most recent visible commit made by the builder (including merges
    /// of its own pull requests). Its tree facts are the current state.
    let head (snapshot: Snapshot) : Commit option =
        builderCommits snapshot |> List.sortBy (fun c -> c.CommittedAt) |> List.tryLast

    let completedRuns (snapshot: Snapshot) = snapshot.Runs |> List.filter (fun r -> r.Completed) |> List.map (fun r -> r.Run)

    /// Fixed checkpoints plus every significant event, in time order, each
    /// labeled with what triggered it. Events are commits, completed CI runs
    /// whose conclusion is not "cancelled", and the end of the window.
    let evaluationPoints (mode: Visibility) (fixedMinutes: int list) (endMinute: int) (timeline: Timeline) : (DateTimeOffset * string) list =
        let clock = timeline.Clock
        let limit = atMinute clock endMinute
        let fixedPoints = fixedMinutes |> List.map (fun m -> atMinute clock m, $"checkpoint T+{m}")
        let commitPoints =
            timeline.Commits
            |> List.filter (fun c -> c.Role <> CommitRole.Owner)
            |> List.choose (fun c -> commitVisibleAt mode c |> Option.map (fun t -> t, $"commit {c.Sha.Substring(0, min 7 c.Sha.Length)}"))
        let runPoints =
            timeline.Runs
            |> List.filter (fun r -> r.Conclusion <> "cancelled")
            |> List.choose (fun r -> r.CompletedAt |> Option.map (fun t -> t, $"CI {r.Workflow} {r.Conclusion} at {r.HeadSha.Substring(0, min 7 r.HeadSha.Length)}"))
        fixedPoints @ commitPoints @ runPoints
        |> List.filter (fun (t, _) -> t >= clock.T0 && t <= limit)
        |> List.groupBy fst
        |> List.map (fun (t, items) -> t, items |> List.map snd |> List.distinct |> String.concat "; ")
        |> List.sortBy fst
