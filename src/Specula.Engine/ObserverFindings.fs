namespace Specula.Engine

open System
open System.Text.Json

/// Rehearsal observer findings (`indy-init.observer-findings/1`), written by the
/// read-only observer `requirements-compiler observe` (kemiller2002/Indy-init,
/// tools/requirements-compiler/src/Observer.fs) and published by the owner to an
/// observer repository. Specula only displays them; it re-counts the findings
/// itself and refuses a file whose own summary disagrees with its findings, that
/// does not declare itself read-only, or that uses an unknown severity, rather
/// than showing a guess.
module ObserverFindings =

    let schema = "indy-init.observer-findings/1"
    let path = "evidence/observer/findings.json"

    type Severity =
        | Blocking
        | Warning

    type Finding =
        { Code: string
          Severity: Severity
          Detail: string
          Evidence: string list
          Requirements: string list
          Recommendation: string
          /// The KICKOFF-EXECUTION-PACKET §8 stop-the-line rule the finding matches, if any.
          StopTheLine: string option }

    type Report =
        { ObservedAt: DateTimeOffset
          Deadline: DateTimeOffset option
          Findings: Finding list
          Blocking: int
          Warnings: int }

    let private strings (name: string) (e: JsonElement) =
        match Json.prop name e with
        | Some a when a.ValueKind = JsonValueKind.Array -> a.EnumerateArray() |> Seq.choose Json.asString |> List.ofSeq
        | _ -> []

    let private text (name: string) (e: JsonElement) = Json.prop name e |> Option.bind Json.asString
    let private time (name: string) (e: JsonElement) =
        text name e |> Option.bind (fun s -> match DateTimeOffset.TryParse s with | true, t -> Some t | _ -> None)

    let private finding (e: JsonElement) : Result<Finding, string> =
        let severity =
            match text "severity" e with
            | Some "blocking" -> Ok Blocking
            | Some "warning" -> Ok Warning
            | Some other -> Error $"unknown severity \"{other}\""
            | None -> Error "a finding has no severity"
        match text "code" e, severity with
        | None, _ | Some "", _ -> Error "a finding has no code"
        | _, Error reason -> Error reason
        | Some code, Ok s ->
            Ok { Code = code
                 Severity = s
                 Detail = text "detail" e |> Option.defaultValue ""
                 Evidence = strings "evidence" e
                 Requirements = strings "requirements" e
                 Recommendation = text "recommendation" e |> Option.defaultValue ""
                 StopTheLine = Json.prop "stopTheLine" e |> Option.bind (text "rule") }

    /// Parses an untrusted findings file. Any inconsistency makes the whole file unreadable.
    let parse (json: JsonElement) : Result<Report, string> =
        let items =
            match Json.prop "findings" json with
            | Some a when a.ValueKind = JsonValueKind.Array -> Ok(a.EnumerateArray() |> List.ofSeq)
            | _ -> Error "no findings array"
        match items, time "observedAt" json with
        | Error reason, _ -> Error reason
        | _, None -> Error "no observedAt timestamp"
        | Ok xs, Some observedAt ->
            match Json.prop "readOnly" json with
            | Some b when b.ValueKind = JsonValueKind.True ->
                let parsed = xs |> List.map finding
                match parsed |> List.tryPick (function Error r -> Some r | Ok _ -> None) with
                | Some reason -> Error reason
                | None ->
                    let findings = parsed |> List.choose (function Ok f -> Some f | Error _ -> None)
                    let blocking = findings |> List.filter (fun f -> f.Severity = Blocking) |> List.length
                    match Json.prop "blocking" json |> Option.bind Json.asInt64 with
                    | Some claimed when claimed <> int64 blocking -> Error $"the file says {claimed} blocking finding(s), but lists {blocking}"
                    | _ ->
                        Ok { ObservedAt = observedAt
                             Deadline = time "deadline" json
                             Findings = findings
                             Blocking = blocking
                             Warnings = findings.Length - blocking }
            | _ -> Error "the observer does not declare itself read-only"
