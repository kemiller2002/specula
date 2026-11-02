namespace Specula.Engine

open System
open System.Text.Json

/// Read-only view of Ordo decision events (`ordo.decision-event/1`, one JSON
/// object per line), as written by `sde ecir recognize --events` and
/// `sde events record-*` (kemiller2002/ordo docs/architecture/ordo-decision-events.md).
///
/// Specula re-derives the counts itself rather than trusting a summary file,
/// and keeps Ordo's rules: evaluation, selection, execution and verification
/// are separate facts; a selection is never shown as executed, an execution
/// never as verified; anything missing is counted as unknown, never as zero.
module OrdoDecisions =

    let schema = "ordo.decision-event/1"
    let path = "evidence/ordo/decision-events.jsonl"

    type Event =
        { Id: string
          Kind: string
          RecognitionId: string option
          DecisionId: string option
          PacketId: string option
          WorkItemId: string option
          /// matchStatus, disposition, status or verdict, by kind.
          Value: string option
          Families: string list
          Patterns: string list
          /// For recognition.run: how many requirements the run says it evaluated.
          Evaluated: int64 option }

    type Summary =
        { Runs: int
          IncompleteRuns: int
          Evaluated: int
          ByStatus: (string * int) list
          Matched: int
          /// None when nothing was evaluated: a rate of nothing is unknown, not 0%.
          MatchRate: float option
          ByFamily: (string * int) list
          ByPattern: (string * int) list
          Selections: (string * int) list
          FallbackToAgent: int
          ExecutionsReported: (string * int) list
          ExecutionUnknown: int
          Verified: (string * int) list
          ExecutedButUnverified: int
          Uncorrelated: int
          CorruptLines: int
          UnsupportedLines: int
          Duplicates: int }

    type LineResult =
        | Good of Event
        | Corrupt
        | OtherSchema

    let private refLabel (p: JsonElement) =
        match Json.str "id" p, Json.int64 "version" p with
        | Some id, Some v -> Some $"{id}@{v}"
        | _ -> None

    let parseLine (line: string) : LineResult =
        match Json.tryParse line with
        | None -> Corrupt
        | Some json ->
            match Json.str "schemaVersion" json with
            | Some s when s <> schema -> OtherSchema
            | None -> Corrupt
            | Some _ ->
                match Json.str "eventId" json, Json.str "kind" json, Json.prop "data" json with
                | Some id, Some kind, Some data ->
                    let corr name = Json.strAt [ "correlation"; name ] json
                    let value =
                        [ "matchStatus"; "disposition"; "status"; "verdict" ] |> List.tryPick (fun n -> Json.str n data)
                    let patterns =
                        match kind with
                        | "pattern.evaluated" -> Json.array "patterns" data
                        | "pattern.selection" -> Json.array "selected" data @ Json.array "generatedBy" data
                        | _ -> []
                    Good
                        { Id = id
                          Kind = kind
                          RecognitionId = corr "recognitionId"
                          DecisionId = corr "decisionId"
                          PacketId = corr "packetId"
                          WorkItemId = corr "workItemId"
                          Value = value
                          Families =
                            if kind = "pattern.evaluated" then
                                patterns |> List.map (fun p -> Json.str "family" p |> Option.defaultValue "unknown family")
                            else []
                          Patterns = patterns |> List.choose refLabel
                          Evaluated = Json.int64 "evaluated" data |> Option.orElse (Json.path [ "counts"; "evaluated" ] data |> Option.bind Json.asInt64) }
                | _ -> Corrupt

    let private countBy (values: string list) = values |> List.countBy id |> List.sortBy fst

    let summarize (text: string) : Summary =
        let results =
            text.Replace("\r\n", "\n").Split('\n')
            |> Array.toList
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.map parseLine
        let parsed = results |> List.choose (function Good e -> Some e | _ -> None)
        let events = parsed |> List.distinctBy (fun e -> e.Id)
        let ofKind k = events |> List.filter (fun e -> e.Kind = k)
        let runs = ofKind "recognition.run"
        let evaluations = ofKind "pattern.evaluated"
        let selections = ofKind "pattern.selection"
        let executions = ofKind "agent.execution"
        let verifications = ofKind "verification.result"
        let value (e: Event) = e.Value |> Option.defaultValue "unrecorded"
        let decisions = selections |> List.choose (fun s -> s.DecisionId |> Option.map (fun d -> d, s)) |> Map.ofList
        let decisionsOf (e: Event) =
            match e.DecisionId, e.PacketId, e.WorkItemId with
            | Some d, _, _ when decisions.ContainsKey d -> [ d ]
            | _, Some p, _ -> decisions |> Map.filter (fun _ s -> s.PacketId = Some p) |> Map.keys |> List.ofSeq
            | _, _, Some w -> decisions |> Map.filter (fun _ s -> s.WorkItemId = Some w) |> Map.keys |> List.ofSeq
            | _ -> []
        let executed = executions |> List.collect decisionsOf |> Set.ofList
        let verified = verifications |> List.collect decisionsOf |> Set.ofList
        let actionable =
            decisions |> Map.filter (fun _ s -> s.Value = Some "pattern-selected" || s.Value = Some "fallback-agent")
        let evaluated = evaluations.Length
        let matched = evaluations |> List.filter (fun e -> e.Value = Some "matched") |> List.length
        { Runs = runs.Length
          IncompleteRuns =
            runs
            |> List.filter (fun r ->
                let found = evaluations |> List.filter (fun e -> r.RecognitionId.IsSome && e.RecognitionId = r.RecognitionId) |> List.length
                r.Evaluated |> Option.exists (fun n -> int64 found < n))
            |> List.length
          Evaluated = evaluated
          ByStatus = evaluations |> List.map value |> countBy
          Matched = matched
          MatchRate = if evaluated = 0 then None else Some(float matched / float evaluated)
          ByFamily = evaluations |> List.collect (fun e -> e.Families) |> countBy
          ByPattern = evaluations |> List.collect (fun e -> e.Patterns) |> countBy
          Selections = selections |> List.map value |> countBy
          FallbackToAgent = selections |> List.filter (fun s -> s.Value = Some "fallback-agent") |> List.length
          ExecutionsReported = executions |> List.map value |> countBy
          ExecutionUnknown = actionable |> Map.filter (fun d _ -> not (executed.Contains d)) |> Map.count
          Verified = verifications |> List.filter (decisionsOf >> List.isEmpty >> not) |> List.map value |> countBy
          ExecutedButUnverified = executed |> Set.filter (verified.Contains >> not) |> Set.count
          Uncorrelated = executions @ verifications |> List.filter (decisionsOf >> List.isEmpty) |> List.length
          CorruptLines = results |> List.filter (function Corrupt -> true | _ -> false) |> List.length
          UnsupportedLines = results |> List.filter (function OtherSchema -> true | _ -> false) |> List.length
          Duplicates = parsed.Length - events.Length }

    /// Historical comparison: the Rehearsal 2–7 audit, as recorded. This is an
    /// audit snapshot, not live data; Specula does not re-derive it.
    type RehearsalRecord =
        { Rehearsal: string
          Repository: string
          Head: string
          PatternRecognition: string
          OtherOrdoUse: string
          Telemetry: string }

    let auditSource =
        "https://github.com/kemiller2002/ordo/blob/cabf72dce9972f5c7e271c0cb423a0042697f533/docs/observability/rehearsal-evidence-matrix.md"

    let auditDate = "2026-11-02"

    let rehearsalAudit : RehearsalRecord list =
        [ { Rehearsal = "R2"; Repository = "kemiller2002/indy-init-rehearsal-002"; Head = "b053d1d"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Confirmed: ordo init / verify (1.17.0)"; Telemetry = "No token or tool-call data" }
          { Rehearsal = "R3"; Repository = "kemiller2002/indy-init-rehearsal-003"; Head = "59cf4d2"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Confirmed: ordo init / verify (1.17.0)"; Telemetry = "Tokens recorded; no tool-call counts" }
          { Rehearsal = "R4"; Repository = "kemiller2002/indy-init-rehearsal-004"; Head = "78a1b15"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Confirmed: install only (1.17.0)"; Telemetry = "Not measurable (recorded)" }
          { Rehearsal = "R5"; Repository = "kemiller2002/indy-init-rehearsal-005"; Head = "b972cb4"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Confirmed: ordo verification schedule in CI (1.18.0)"; Telemetry = "No token or tool-call data" }
          { Rehearsal = "R6"; Repository = "kemiller2002/indy-init-rehearsal-006"; Head = "41fb6d6"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Confirmed: install, status, CI install (1.19.0)"; Telemetry = "No token or tool-call data" }
          { Rehearsal = "R7"; Repository = "kemiller2002/indy-init-rehearsal-007"; Head = "f61493f"
            PatternRecognition = "Unknown"; OtherOrdoUse = "Unknown: recorded as locally unavailable"; Telemetry = "Tokens and tool calls recorded" } ]
