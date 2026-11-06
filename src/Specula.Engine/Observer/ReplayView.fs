namespace Specula.Engine.Observer

open System
open System.Text.Json
open Specula.Engine

/// The observer screen's data: the replay view written by the observer's
/// replay command (specula.observer-replay-view/1). It is historical data,
/// read as untrusted JSON; nothing here computes findings.
module ReplayView =

    let schema = "specula.observer-replay-view/1"
    let path = "observer/replay-009.json"

    type AlertLine =
        { Severity: string
          Rule: string
          Title: string
          Intervention: bool }

    type CheckpointView =
        { Label: string
          Minute: int
          At: DateTimeOffset option
          Trigger: string
          Head: string option
          LastCommitAt: DateTimeOffset option
          Elapsed: int
          Remaining: int
          Mandatory: int
          CodeCommitted: int
          Demonstrated: int
          Verified: int
          VerifiedCatalog: int
          VerifiedFrozenPlan: int
          FrozenPlanMandatory: int
          CatalogMandatory: int
          Blockers: string list
          Alerts: AlertLine list
          NewAlerts: int
          Recovery: string
          RecoveryMissing: string list
          RecommendedAction: string }

    type ModeView = { Mode: string; Checkpoints: CheckpointView list }

    type HandoffView =
        { At: DateTimeOffset option
          CurrentCommit: string option
          RemainingMandatory: int
          NextActions: string list
          Consistent: bool }

    type Report =
        { Repository: string
          T0: DateTimeOffset option
          Deadline: DateTimeOffset option
          Budget: int
          ScopeDigest: string
          Modes: ModeView list
          Handoff: HandoffView option }

    let private i name el = Json.int64 name el |> Option.map int |> Option.defaultValue 0
    let private s name el = Json.str name el |> Option.defaultValue ""
    let private ss name el = Json.array name el |> List.choose Json.asString

    let private checkpoint (el: JsonElement) : CheckpointView =
        { Label = s "label" el
          Minute = i "minute" el
          At = Json.time "at" el
          Trigger = s "trigger" el
          Head = Json.str "head" el
          LastCommitAt = Json.time "lastCommitAt" el
          Elapsed = i "elapsedMinutes" el
          Remaining = i "remainingMinutes" el
          Mandatory = i "mandatory" el
          CodeCommitted = i "codeCommitted" el
          Demonstrated = i "demonstrated" el
          Verified = i "verified" el
          VerifiedCatalog = i "verifiedCatalog" el
          VerifiedFrozenPlan = i "verifiedFrozenPlan" el
          FrozenPlanMandatory = i "frozenPlanMandatory" el
          CatalogMandatory = i "catalogMandatory" el
          Blockers = ss "blockers" el
          Alerts =
            Json.array "alerts" el
            |> List.map (fun a ->
                { Severity = s "severity" a
                  Rule = s "rule" a
                  Title = s "title" a
                  Intervention = Json.bool "intervention" a |> Option.defaultValue false })
          NewAlerts = i "newAlerts" el
          Recovery = s "recovery" el
          RecoveryMissing = ss "recoveryMissing" el
          RecommendedAction = s "recommendedAction" el }

    let parse (text: string) : Result<Report, string> =
        match Json.tryParse text with
        | None -> Error "not valid JSON"
        | Some root when Json.str "schema" root <> Some schema -> Error $"schema is not {schema}"
        | Some root ->
            let modes =
                Json.array "modes" root
                |> List.map (fun m -> { Mode = s "mode" m; Checkpoints = Json.array "checkpoints" m |> List.map checkpoint })
            if modes |> List.forall (fun m -> m.Checkpoints.IsEmpty) then Error "no checkpoints"
            else
                Ok
                    { Repository = s "repository" root
                      T0 = Json.time "t0" root
                      Deadline = Json.time "deadline" root
                      Budget = i "budgetMinutes" root
                      ScopeDigest = s "scopeDigest" root
                      Modes = modes
                      Handoff =
                        Json.prop "handoff" root
                        |> Option.map (fun h ->
                            { At = Json.time "at" h
                              CurrentCommit = Json.str "currentCommit" h
                              RemainingMandatory = i "remainingMandatory" h
                              NextActions = ss "nextActions" h
                              Consistent = Json.bool "consistent" h |> Option.defaultValue false }) }

    /// Clamp a requested checkpoint index into the mode's range; default to
    /// the last checkpoint.
    let selectIndex (mode: ModeView) (requested: int option) =
        let n = mode.Checkpoints.Length
        match requested with
        | Some k when k >= 0 && k < n -> k
        | _ -> max 0 (n - 1)

    let modeOf (report: Report) (code: string) =
        report.Modes |> List.tryFind (fun m -> m.Mode = code) |> Option.orElse (List.tryHead report.Modes)
