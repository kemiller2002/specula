namespace Specula.Engine.Observer

open System
open Specula.Engine.Observer.Types

/// Continuation handoff for a replacement builder, generated only from the
/// evidence visible at the handoff instant, and its consistency check.
module Handoff =

    let private short (sha: string) = sha.Substring(0, min 7 sha.Length)

    let generate (scope: ScopeManifest) (snapshot: Snapshot) (findings: Finding list) : Handoff =
        let ctx = Rules.context scope snapshot
        let p = Rules.progress ctx
        let remainingReqs = Rules.unverifiedFrozen ctx @ Rules.unverifiedCatalog ctx
        let facts = ctx.Facts
        let verified =
            Set.union ctx.VerifiedCatalog ctx.VerifiedFrozen |> Set.toList |> List.sort
        let completed =
            [ yield!
                  Replay.builderCommits snapshot
                  |> List.collect (fun c -> c.Capabilities)
                  |> List.distinct
              if p.LiveRoundTripWrites > 0 then $"Live GitHub round trip exercised by CI ({p.LiveRoundTripWrites} application writes to the data branch)" ]
        let blockers =
            findings
            |> List.filter (fun f -> f.Rule = "OBS-005")
            |> List.map (fun f -> $"{f.Title}. {f.Why}")
        let violated =
            facts |> Option.map (Rules.violatedContracts scope) |> Option.defaultValue []
        let due = Rules.tasksDue ctx |> List.filter (fun (_, m, a) -> not a && not m.IsEmpty) |> List.map (fun (t, _, _) -> t)
        let nextTasks =
            scope.Tasks
            |> List.filter (fun t -> due |> List.exists (fun d -> d.Id = t.Id))
            |> List.sortBy (fun t -> t.LatestStart)
            |> List.truncate 6
        let nextActions =
            [ "Do not restart the clock and do not discard the existing application: continue from the current commit on a new branch."
              if not blockers.IsEmpty then
                  "Open the Verification gate artifact's suite run.log and determine, for each refused suite, whether the plan's command is broken (record it and ask the owner for a decision; do not edit frozen inputs) or the plan's test projects are missing (write them)."
              yield!
                  nextTasks
                  |> List.map (fun t -> $"Begin {t.Id} \"{t.Title}\" (owner role {t.Owner}, latest start T+{t.LatestStart}, estimate {t.EstimateMinutes} min).")
              if not violated.IsEmpty then
                  $"Adopt the Forma patterns the layout contract names ({Rules.commas (violated |> List.map (fun (c, _) -> c.Id))}) in web/index.html; keep existing data-requirement-id bindings so catalog verification stays green."
              if facts |> Option.exists (fun f -> not f.FolioUsed) then "Build the report preview and Folio export (T26); Folio is a required component."
              $"Re-run Requirement verification after every merge so the catalog result ({p.VerifiedCatalog} of {p.CatalogMandatory} mandatory catalog requirements verified at hand-off) does not regress."
              "Register the continuation as a Praxis work item before the first commit, and record a checkpoint with the next action." ]
        { GeneratedAt = snapshot.At
          T0 = snapshot.Clock.T0
          Deadline = snapshot.Clock.Deadline
          RemainingMinutes = p.RemainingMinutes
          CurrentCommit = ctx.Head |> Option.map (fun c -> c.Sha)
          CompletedFunctionality = completed
          VerifiedRequirements = verified
          NotApplicable = ctx.NotApplicable |> Set.toList |> List.sort
          RemainingMandatory = remainingReqs |> List.map (fun r -> r.Id)
          RemainingByArea = remainingReqs |> List.countBy (fun r -> r.Area) |> List.sortByDescending snd
          Blockers = blockers
          NextActions = nextActions
          PreserveInstructions =
            [ "Keep every commit on main and the claude/* branch; never force-push, reset or rewrite history."
              "Keep the catalog verification manifest, its pin and the requirement-implementation map; extend, do not replace."
              "Keep the causa-data branch; it holds the live round-trip records."
              "Do not edit frozen requirement packages, governance-checked workflows or approval records." ]
          ClockInstructions =
            [ $"Original T0: {snapshot.Clock.T0:u} ({snapshot.Clock.Source})."
              $"Original deadline: {snapshot.Clock.Deadline:u}. It does not move."
              "Do not record a new T0, do not restart the seven-hour budget, and report all times against the original T0." ]
          EvidenceBasis =
            [ $"Snapshot at {snapshot.At:u} ({visibilityCode snapshot.Mode} visibility), {snapshot.Commits.Length} visible commits, {snapshot.Runs.Length} visible CI runs"
              "The builder's final report is sealed from the observer and was not used" ] }

    /// Internal consistency with the evidence at the handoff instant.
    /// Returns the list of violations (empty when consistent).
    let validate (scope: ScopeManifest) (snapshot: Snapshot) (h: Handoff) : string list =
        let ctx = Rules.context scope snapshot
        let mandatory = ctx.Mandatory |> List.map (fun r -> r.Id) |> Set.ofList
        let verifiedNow = Set.union ctx.VerifiedCatalog ctx.VerifiedFrozen
        let visible = snapshot.Commits |> List.map (fun c -> c.Sha) |> Set.ofList
        [ if h.T0 <> snapshot.Clock.T0 then "T0 differs from the authoritative clock"
          if h.Deadline <> snapshot.Clock.Deadline then "deadline differs from the authoritative clock"
          if h.Deadline <> h.T0.AddMinutes(float snapshot.Clock.BudgetMinutes) then "deadline is not T0 plus the original budget"
          match h.CurrentCommit with
          | Some sha when not (visible.Contains sha) -> $"current commit {short sha} was not visible at the handoff instant"
          | None -> "no current commit"
          | _ -> ()
          if h.VerifiedRequirements |> List.exists (fun r -> not (verifiedNow.Contains r)) then "a requirement is listed verified without visible evidence"
          if h.RemainingMandatory |> List.exists (fun r -> verifiedNow.Contains r) then "a verified requirement is also listed as remaining"
          if h.RemainingMandatory |> List.exists (fun r -> not (mandatory.Contains r)) then "a non-mandatory requirement is listed as remaining mandatory"
          if h.NotApplicable |> List.exists (fun r -> not (ctx.NotApplicable.Contains r)) then "a requirement is listed not applicable without verifier evidence"
          let accounted =
              Set.unionMany [ Set.ofList h.VerifiedRequirements |> Set.intersect mandatory; Set.ofList h.RemainingMandatory; Set.ofList h.NotApplicable |> Set.intersect mandatory ]
          if accounted <> mandatory then $"verified + not applicable + remaining ({accounted.Count}) does not account for every mandatory requirement ({mandatory.Count})"
          if h.RemainingByArea |> List.sumBy snd <> h.RemainingMandatory.Length then "area counts do not sum to the remaining requirements"
          if h.RemainingMinutes <> max 0 (int (floor (h.Deadline - h.GeneratedAt).TotalMinutes)) then "remaining minutes do not match the deadline"
          if not (h.ClockInstructions |> List.exists (fun i -> i.Contains "Do not record a new T0")) then "no instruction not to restart the clock"
          if h.PreserveInstructions.IsEmpty then "no instruction to preserve existing work" ]
