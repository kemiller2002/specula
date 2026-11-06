namespace Specula.Engine.Observer

open System
open Specula.Engine.Observer.Types

/// Alert emission with deduplication. A finding raises an alert when it is
/// new, when its severity changes, or when its material evidence (its
/// fingerprint) changes. A Warning or Critical observation that disappears
/// raises one informational "resolved" alert. Anything else is suppressed.
module Alerts =

    type State =
        { Active: Map<string, Finding>
          Sequence: int }

    let empty = { Active = Map.empty; Sequence = 0 }

    let private change (previous: Finding option) (current: Finding) : AlertChange option =
        match previous with
        | None -> Some AlertChange.New
        | Some p when severityRank current.Severity > severityRank p.Severity -> Some AlertChange.Escalated
        | Some p when severityRank current.Severity < severityRank p.Severity -> Some AlertChange.Deescalated
        | Some p when p.Fingerprint <> current.Fingerprint -> Some AlertChange.EvidenceChanged
        | Some _ -> None

    let private resolvedFinding (f: Finding) : Finding =
        { f with
            Severity = Severity.Informational
            Title = $"Resolved: {f.Title}"
            Evidence = [ "The condition is no longer observed at this point." ] @ f.Evidence
            Action = "None."
            Intervention = false
            Fingerprint = "resolved" }

    /// Compare one evaluation's findings with the active set and return the
    /// alerts to send and the next state. `prefix` distinguishes replay modes.
    let step (prefix: string) (clock: Clock) (at: DateTimeOffset) (findings: Finding list) (state: State) : Alert list * State =
        let minute = Replay.minuteOf clock at
        let current = findings |> List.map (fun f -> f.Key, f) |> Map.ofList
        let raised =
            findings
            |> List.choose (fun f -> change (state.Active |> Map.tryFind f.Key) f |> Option.map (fun c -> c, f))
        let resolved =
            state.Active
            |> Map.toList
            |> List.filter (fun (k, f) ->
                not (current.ContainsKey k) && f.Kind = FindingKind.Observation && f.Severity <> Severity.Informational)
            |> List.map (fun (_, f) -> AlertChange.Resolved, resolvedFinding f)
        let all = raised @ resolved
        let alerts =
            all
            |> List.mapi (fun i (c, f) ->
                { Id = sprintf "%s-%04d" prefix (state.Sequence + i + 1)
                  At = at
                  Minute = minute
                  Change = c
                  Finding = f })
        alerts, { Active = current; Sequence = state.Sequence + all.Length }
