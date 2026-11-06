namespace Specula.Engine.Observer

open System
open Specula.Engine.Observer.Types

/// Blind historical replay: evaluate every checkpoint in time order, giving
/// the rules only the snapshot of that instant, and thread the alert state
/// forward. Each checkpoint's result is produced before the next snapshot is
/// built, and is never revised afterwards.
module Engine =

    let standardCheckpoints = [ 0; 20; 40; 60; 80; 100; 115 ]

    let evaluateAt (scope: ScopeManifest) (timeline: Timeline) (mode: Visibility) (at: DateTimeOffset) =
        let snapshot = Replay.snapshotAt mode at timeline
        let ctx, findings = Rules.evaluate scope snapshot
        snapshot, ctx, findings

    let private limitations (snapshot: Snapshot) (previous: Snapshot option) =
        let sameAsBefore =
            match previous with
            | Some p -> p.Commits.Length = snapshot.Commits.Length && p.Runs |> List.filter (fun r -> r.Completed) |> List.length = (snapshot.Runs |> List.filter (fun r -> r.Completed) |> List.length)
            | None -> false
        [ if snapshot.Commits |> List.forall (fun c -> c.Role <> CommitRole.Builder) then "No builder commit is visible yet; scope and architecture rules have nothing to evaluate."
          if snapshot.Runs.IsEmpty then "No CI run is visible; build and test status is unknown, not failed."
          if sameAsBefore then "No new commit or completed CI run since the previous evaluation; findings are carried, not re-derived from new evidence."
          if snapshot.Mode = Visibility.Remote then "Remote visibility uses the first CI run on a commit or a descendant as an upper bound on its push time."
          yield! snapshot.Unobservable ]

    /// Replay one visibility mode. `points` are (instant, trigger) pairs in
    /// time order; `labelOf` names a point.
    let replay (scope: ScopeManifest) (timeline: Timeline) (mode: Visibility) (points: (DateTimeOffset * string) list) : CheckpointResult list =
        let prefix = if mode = Visibility.Local then "R9L" else "R9R"
        points
        |> List.fold
            (fun (state: Alerts.State, previous: Snapshot option, results: CheckpointResult list) (at, trigger) ->
                let snapshot, ctx, findings = evaluateAt scope timeline mode at
                let alerts, next = Alerts.step prefix timeline.Clock at findings state
                let minute = Replay.minuteOf timeline.Clock at
                let result =
                    { Label = $"T+{minute}"
                      At = at
                      Minute = minute
                      Mode = mode
                      Trigger = trigger
                      VisibleCommits = snapshot.Commits |> List.map (fun c -> c.Sha)
                      HeadSha = ctx.Head |> Option.map (fun c -> c.Sha)
                      Progress = Rules.progress ctx
                      Findings = findings
                      Alerts = alerts
                      Recovery = Rules.recovery ctx
                      Limitations = limitations snapshot previous }
                next, Some snapshot, result :: results)
            (Alerts.empty, None, [])
        |> fun (_, _, results) -> List.rev results
