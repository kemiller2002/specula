namespace Specula.Engine

open System
open System.Text.Json

/// Versioned evidence adapter registry (SPEC-G-003, CAP-010..012, SPEC-D-002).
///
/// Only allowlisted paths are read, each with an exact schema identifier.
/// Formats were taken from evidence actually published in an observed
/// Echelon rehearsal repository (kemiller2002/indy-init-rehearsal-007 at
/// f61493f). Anything else is reported as missing or unsupported — never
/// guessed.
module Evidence =

    let maxBytes = 512 * 1024

    type AdapterKind =
        | Traceability
        | AcceptanceLedger
        | Kickoff
        | DecisionTable
        | DefectTable

    type Adapter =
        { Id: string
          Kind: AdapterKind
          Path: string
          Schema: string
          Description: string }

    let registry : Adapter list =
        [ { Id = "traceability"; Kind = Traceability; Path = "evidence/traceability.json"; Schema = "causa.traceability/1"
            Description = "Requirement verification status" }
          { Id = "acceptance"; Kind = AcceptanceLedger; Path = "evidence/acceptance/ledger.json"; Schema = "causa.pat-ledger/1"
            Description = "Acceptance test ledger (recorded by the implementing agent)" }
          { Id = "kickoff"; Kind = Kickoff; Path = "evidence/kickoff/kickoff.json"; Schema = "indy-init.rehearsal-kickoff/1"
            Description = "Recorded kickoff and deadline" }
          { Id = "decisions"; Kind = DecisionTable; Path = "evidence/DECISIONS.md"; Schema = "markdown table: ID | Decision | Why | Effect"
            Description = "Recorded decisions" }
          { Id = "defects"; Kind = DefectTable; Path = "evidence/defects.md"; Schema = "markdown table: ID | Defect | Found by | Fix | Guard"
            Description = "Recorded defects and their corrections" } ]

    let adapterVersion = "specula.evidence-adapters/1"

    type Requirement =
        { Id: string
          Title: string
          Priority: string option
          RawStatus: string
          State: EvidenceState
          Evidence: string
          Note: string }

    type AcceptanceResult =
        { Id: string
          Requirement: string option
          RawResult: string
          State: EvidenceState
          Runs: string list
          Note: string }

    type Decision = { Id: string; Text: string; Why: string; Effect: string }

    type Correction = { Id: string; Defect: string; FoundBy: string; Fix: string; Guard: string }

    type RecordedKickoff =
        { T0: DateTimeOffset
          Deadline: DateTimeOffset option
          LimitMinutes: int64 option }

    type Payload =
        | Requirements of scope: string option * Requirement list
        | Acceptance of candidate: string option * AcceptanceResult list
        | KickoffRecord of RecordedKickoff
        | Decisions of Decision list
        | Corrections of Correction list

    type SourceOutcome =
        | NotFetched
        | Parsed of Payload
        | Missing
        | Unsupported of found: string
        | Invalid of reason: string
        | TooLarge of bytes: int
        | FetchFailed of reason: string

    type Source =
        { Adapter: Adapter
          Sha: string
          Outcome: SourceOutcome }

    // ---- status mapping ----

    let requirementState (status: string) : EvidenceState =
        match status.Trim().ToUpperInvariant() with
        | "VERIFIED" -> Verified
        | "FAILED" | "FAIL" -> FailedEvidence
        | "BLOCKED" -> Blocked
        | "PARTIAL" | "NOT_IMPLEMENTED" | "NOT_RUN" -> Unverified
        | "NOT_ASSESSED" -> Unavailable "Not assessed by the source"
        | other -> Unavailable $"Unrecognized status \"{other}\""

    let acceptanceState (result: string) : EvidenceState =
        match result.Trim().ToUpperInvariant() with
        | "PASS" | "PASSED" -> Verified
        | "FAIL" | "FAILED" -> FailedEvidence
        | "BLOCKED" -> Blocked
        | "NOT_RUN" | "NOT RUN" | "PARTIAL" -> Unverified
        | other -> Unavailable $"Unrecognized result \"{other}\""

    let stateLabel state =
        match state with
        | Verified -> "Verified"
        | FailedEvidence -> "Failed"
        | Blocked -> "Blocked"
        | Unverified -> "Unverified"
        | Unavailable _ -> "Unavailable"

    let stateLozenge state =
        match state with
        | Verified -> "ok"
        | FailedEvidence | Blocked -> "blocked"
        | Unverified -> "attention"
        | Unavailable _ -> "unknown"

    // ---- JSON adapters ----

    let private schemaOf json = Json.str "schema" json

    let private withSchema (adapter: Adapter) (json: JsonElement) (parse: JsonElement -> SourceOutcome) =
        match schemaOf json with
        | Some s when s = adapter.Schema -> parse json
        | Some other -> Unsupported other
        | None -> Unsupported "no \"schema\" field"

    let parseTraceability (json: JsonElement) : SourceOutcome =
        let requirements =
            Json.array "requirements" json
            |> List.choose (fun r ->
                match Json.str "id" r, Json.str "status" r with
                | Some id, Some status ->
                    Some
                        { Id = id
                          Title = Json.str "title" r |> Option.defaultValue ""
                          Priority = Json.str "priority" r
                          RawStatus = status
                          State = requirementState status
                          Evidence = Json.str "evidence" r |> Option.defaultValue ""
                          Note = Json.str "note" r |> Option.defaultValue "" }
                | _ -> None)
        match Json.prop "requirements" json with
        | None -> Invalid "\"requirements\" array is missing"
        | Some _ -> Parsed(Requirements(Json.str "scope" json, requirements))

    let parseAcceptance (json: JsonElement) : SourceOutcome =
        match Json.prop "results" json with
        | None -> Invalid "\"results\" array is missing"
        | Some _ ->
            let results =
                Json.array "results" json
                |> List.choose (fun r ->
                    match Json.str "pat" r |> Option.orElse (Json.str "id" r), Json.str "result" r with
                    | Some id, Some result ->
                        Some
                            { Id = id
                              Requirement = Json.str "requirement" r
                              RawResult = result
                              State = acceptanceState result
                              Runs = Json.array "runs" r |> List.choose Json.asString
                              Note = Json.str "note" r |> Option.defaultValue "" }
                    | _ -> None)
            Parsed(Acceptance(Json.str "candidate" json, results))

    let parseKickoff (json: JsonElement) : SourceOutcome =
        match Json.time "t0" json with
        | None -> Invalid "\"t0\" timestamp is missing or malformed"
        | Some t0 ->
            Parsed(
                KickoffRecord
                    { T0 = t0
                      Deadline = Json.time "deadline" json
                      LimitMinutes = Json.int64 "timeLimitMinutes" json }
            )

    // ---- Markdown table adapters ----

    let private cells (line: string) : string list option =
        let t = line.Trim()
        if t.StartsWith "|" && t.EndsWith "|" && t.Length >= 2 then
            t.Substring(1, t.Length - 2).Split('|') |> Array.map (fun c -> c.Trim()) |> Array.toList |> Some
        else None

    let private isSeparator (row: string list) =
        row |> List.forall (fun c -> c <> "" && c |> Seq.forall (fun ch -> ch = '-' || ch = ':'))

    let private normalizeHeader (row: string list) = row |> List.map (fun c -> c.ToLowerInvariant())

    /// Rows of every table whose header equals `header` (case-insensitive).
    let tableRows (header: string list) (markdown: string) : string list list =
        let expected = header |> List.map (fun h -> h.ToLowerInvariant())
        let lines = markdown.Replace("\r\n", "\n").Split('\n') |> Array.toList
        let rec scan (inTable: bool) (acc: string list list) (rest: string list) =
            match rest with
            | [] -> List.rev acc
            | line :: tail ->
                match cells line, inTable with
                | Some row, false when normalizeHeader row = expected -> scan true acc tail
                | Some row, true when isSeparator row -> scan true acc tail
                | Some row, true when List.length row = List.length expected -> scan true (row :: acc) tail
                | Some _, true -> scan true acc tail
                | None, _ -> scan false acc tail
                | Some _, false -> scan false acc tail
        scan false [] lines

    let decisionHeader = [ "ID"; "Decision"; "Why"; "Effect" ]
    let defectHeader = [ "ID"; "Defect"; "Found by"; "Fix"; "Guard" ]

    let parseDecisions (markdown: string) : SourceOutcome =
        match tableRows decisionHeader markdown with
        | [] -> Unsupported "no table with header ID | Decision | Why | Effect"
        | rows ->
            rows
            |> List.choose (function
                | [ id; text; why; effect ] when id <> "" -> Some { Id = id; Text = text; Why = why; Effect = effect }
                | _ -> None)
            |> Decisions
            |> Parsed

    let parseDefects (markdown: string) : SourceOutcome =
        match tableRows defectHeader markdown with
        | [] -> Unsupported "no table with header ID | Defect | Found by | Fix | Guard"
        | rows ->
            rows
            |> List.choose (function
                | [ id; defect; foundBy; fix; guard ] when id <> "" ->
                    Some { Id = id; Defect = defect; FoundBy = foundBy; Fix = fix; Guard = guard }
                | _ -> None)
            |> Corrections
            |> Parsed

    /// Parse one fetched file with its adapter. Input is untrusted text.
    let parse (adapter: Adapter) (text: string) : SourceOutcome =
        if text.Length > maxBytes then TooLarge text.Length
        else
            match adapter.Kind with
            | DecisionTable -> parseDecisions text
            | DefectTable -> parseDefects text
            | Traceability | AcceptanceLedger | Kickoff ->
                match Json.tryParse text with
                | None -> Invalid "not valid JSON"
                | Some json ->
                    match adapter.Kind with
                    | Traceability -> withSchema adapter json parseTraceability
                    | AcceptanceLedger -> withSchema adapter json parseAcceptance
                    | _ -> withSchema adapter json parseKickoff

    let rawUrl (repo: RepoId) (sha: string) (path: string) =
        $"https://raw.githubusercontent.com/{Uri.EscapeDataString repo.Owner}/{Uri.EscapeDataString repo.Name}/{sha}/{path}"

    let blobUrl (repo: RepoId) (sha: string) (path: string) = $"{Repo.repoUrl repo}/blob/{sha}/{path}"

    // ---- views over a set of sources ----

    let private pick (f: Payload -> 'a option) (sources: Source list) : 'a option =
        sources |> List.tryPick (fun s -> match s.Outcome with Parsed p -> f p | _ -> None)

    let requirements sources = sources |> pick (function Requirements(_, r) -> Some r | _ -> None)
    let acceptance sources = sources |> pick (function Acceptance(_, r) -> Some r | _ -> None)
    let decisions sources = sources |> pick (function Decisions d -> Some d | _ -> None)
    let corrections sources = sources |> pick (function Corrections c -> Some c | _ -> None)
    let kickoff sources = sources |> pick (function KickoffRecord k -> Some k | _ -> None)

    let outcomeLabel outcome =
        match outcome with
        | NotFetched -> "Not checked"
        | Parsed(Requirements(_, r)) -> $"Supported · {List.length r} requirements"
        | Parsed(Acceptance(_, r)) -> $"Supported · {List.length r} results"
        | Parsed(KickoffRecord _) -> "Supported · kickoff recorded"
        | Parsed(Decisions d) -> $"Supported · {List.length d} decisions"
        | Parsed(Corrections c) -> $"Supported · {List.length c} corrections"
        | Missing -> "Not found"
        | Unsupported found -> $"Unsupported schema ({found})"
        | Invalid reason -> $"Parse error: {reason}"
        | TooLarge bytes -> $"Too large ({bytes} bytes; limit {maxBytes})"
        | FetchFailed reason -> $"Unavailable: {reason}"

    let outcomeLozenge outcome =
        match outcome with
        | Parsed _ -> "ok"
        | Missing | NotFetched -> "unknown"
        | Unsupported _ | Invalid _ | TooLarge _ | FetchFailed _ -> "attention"

    type Coverage =
        | CoverageSupported
        | CoveragePartial
        | CoverageNotFound
        | CoverageUnavailable of reason: string

    /// Evidence coverage strip (EXPERIENCE §5), distinct from CI status.
    let coverage (sources: Source list) : Coverage =
        let outcomes = sources |> List.map (fun s -> s.Outcome)
        let parsed = outcomes |> List.filter (function Parsed _ -> true | _ -> false) |> List.length
        let problems = outcomes |> List.filter (function Unsupported _ | Invalid _ | TooLarge _ | FetchFailed _ -> true | _ -> false) |> List.length
        match outcomes with
        | [] -> CoverageUnavailable "Evidence has not been checked yet."
        | _ when outcomes |> List.forall (fun o -> o = NotFetched) -> CoverageUnavailable "Evidence has not been checked yet."
        | _ when parsed = 0 && problems = 0 -> CoverageNotFound
        | _ when parsed = 0 -> CoverageUnavailable "Evidence files were found but none uses a supported schema."
        | _ when problems > 0 || requirements sources |> Option.isNone -> CoveragePartial
        | _ -> CoverageSupported

    let coverageLabel c =
        match c with
        | CoverageSupported -> "Supported"
        | CoveragePartial -> "Partial"
        | CoverageNotFound -> "Not found"
        | CoverageUnavailable _ -> "Unavailable"

    let coverageLozenge c =
        match c with
        // Coverage describes whether evidence could be read, not whether it
        // verifies anything, so it never uses the success colour.
        | CoverageSupported -> "unknown"
        | CoveragePartial -> "attention"
        | CoverageNotFound | CoverageUnavailable _ -> "unknown"
