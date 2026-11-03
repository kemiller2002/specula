namespace Specula.Engine

open System
open System.Text.Json

/// Requirement verification reports (dokimos.requirement-verification/1).
///
/// The observed repository publishes one report per verification run at
/// `path`. Specula reads it as untrusted data: an unrecognized status or a
/// requirement without an identifier makes the whole file unreadable rather
/// than silently miscounted. Every count shown is computed here from the
/// requirement list; the file's own summary is only compared against it.
module RequirementVerification =

    let path = "evidence/requirement-verification/requirement-verification.json"
    let schema = "dokimos.requirement-verification/1"

    [<RequireQualifiedAccess>]
    type Status =
        | Pass
        | Fail
        | Blocked
        | NotRun
        | NotApplicable

    let statusOfCode (code: string) : Status option =
        match code.Trim().ToUpperInvariant() with
        | "PASS" -> Some Status.Pass
        | "FAIL" -> Some Status.Fail
        | "BLOCKED" -> Some Status.Blocked
        | "NOT_RUN" -> Some Status.NotRun
        | "NOT_APPLICABLE" -> Some Status.NotApplicable
        | _ -> None

    let statusCode (status: Status) =
        match status with
        | Status.Pass -> "PASS"
        | Status.Fail -> "FAIL"
        | Status.Blocked -> "BLOCKED"
        | Status.NotRun -> "NOT_RUN"
        | Status.NotApplicable -> "NOT_APPLICABLE"

    let statusLabel (status: Status) =
        match status with
        | Status.Pass -> "Pass"
        | Status.Fail -> "Fail"
        | Status.Blocked -> "Blocked"
        | Status.NotRun -> "Not run"
        | Status.NotApplicable -> "Not applicable"

    /// Forma lozenge state. Only a passing requirement uses the success colour.
    let statusLozenge (status: Status) =
        match status with
        | Status.Pass -> "ok"
        | Status.Fail | Status.Blocked -> "blocked"
        | Status.NotRun -> "attention"
        | Status.NotApplicable -> "unknown"

    let allStatuses = [ Status.Pass; Status.Fail; Status.Blocked; Status.NotRun; Status.NotApplicable ]

    type Obligation =
        { Id: string
          Level: string
          Check: string
          Mandatory: bool
          Status: Status
          Reason: string
          Tests: string list }

    type Requirement =
        { Id: string
          Type: string
          Screen: string
          Name: string
          Required: bool
          Status: Status
          FailureClass: string option
          Implemented: bool
          Obligations: Obligation list }

    type Manifest =
        { CatalogId: string option
          ContentHash: string option
          SourceCommit: string option
          Intact: bool option }

    type Acceptance =
        { Accepted: bool option
          EvidenceProblems: string list
          Blockers: string list }

    type Summary =
        { Applicable: int
          Implemented: int
          Verified: int
          Failed: int
          Blocked: int
          NotRun: int
          NotApplicable: int }

    type Report =
        { Manifest: Manifest
          Candidate: string option
          RunId: string option
          Requirements: Requirement list
          Declared: Summary option
          Acceptance: Acceptance }

    // ---- parsing ----

    let private strings name json = Json.array name json |> List.choose Json.asString

    let private text name json = Json.str name json |> Option.defaultValue ""

    let private statusField (owner: string) (json: JsonElement) : Result<Status, string> =
        match Json.str "status" json with
        | None -> Error $"{owner} has no status"
        | Some code ->
            match statusOfCode code with
            | Some status -> Ok status
            | None -> Error $"{owner} has unrecognized status \"{code}\""

    let private sequence (results: Result<'a, string> list) : Result<'a list, string> =
        List.foldBack
            (fun next acc ->
                match next, acc with
                | Ok value, Ok values -> Ok(value :: values)
                | Error e, _ -> Error e
                | Ok _, Error e -> Error e)
            results
            (Ok [])

    let private parseObligation (requirementId: string) (json: JsonElement) : Result<Obligation, string> =
        match Json.str "id" json with
        | None -> Error $"an obligation of {requirementId} has no id"
        | Some id ->
            statusField $"obligation {id}" json
            |> Result.map (fun status ->
                { Id = id
                  Level = text "level" json
                  Check = text "check" json
                  Mandatory = Json.bool "mandatory" json |> Option.defaultValue true
                  Status = status
                  Reason = text "reason" json
                  Tests = strings "tests" json })

    let private parseRequirement (json: JsonElement) : Result<Requirement, string> =
        match Json.str "id" json with
        | None -> Error "a requirement has no id"
        | Some id ->
            match statusField $"requirement {id}" json, Json.bool "implemented" json with
            | Error e, _ -> Error e
            | Ok _, None -> Error $"requirement {id} does not state whether it is implemented"
            | Ok status, Some implemented ->
                Json.array "obligations" json
                |> List.map (parseObligation id)
                |> sequence
                |> Result.map (fun obligations ->
                    { Id = id
                      Type = text "type" json
                      Screen = text "screen" json
                      Name = text "name" json
                      Required = Json.bool "required" json |> Option.defaultValue true
                      Status = status
                      FailureClass = Json.str "failureClass" json |> Option.filter (String.IsNullOrWhiteSpace >> not)
                      Implemented = implemented
                      Obligations = obligations })

    let private parseDeclared (json: JsonElement) : Summary option =
        let count name = Json.int64 name json |> Option.map int
        match count "applicable", count "implemented", count "verified", count "failed", count "blocked", count "notRun", count "notApplicable" with
        | Some a, Some i, Some v, Some f, Some b, Some n, Some na ->
            Some { Applicable = a; Implemented = i; Verified = v; Failed = f; Blocked = b; NotRun = n; NotApplicable = na }
        | _ -> None

    /// Parse a report whose schema identifier has already been checked.
    let parse (json: JsonElement) : Result<Report, string> =
        match Json.prop "requirements" json |> Option.map (fun e -> e.ValueKind) with
        | None -> Error "\"requirements\" array is missing"
        | Some kind when kind <> JsonValueKind.Array -> Error "\"requirements\" is not an array"
        | Some _ ->
            Json.array "requirements" json
            |> List.map parseRequirement
            |> sequence
            |> Result.bind (fun requirements ->
                let ids = requirements |> List.map (fun r -> r.Id)
                match ids |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) with
                | Some(duplicate, _) -> Error $"requirement {duplicate} appears more than once"
                | None ->
                    let manifest = Json.prop "manifest" json
                    let acceptance = Json.prop "acceptance" json
                    Ok
                        { Manifest =
                            { CatalogId = manifest |> Option.bind (Json.str "catalogId")
                              ContentHash = manifest |> Option.bind (Json.str "contentHash")
                              SourceCommit = manifest |> Option.bind (Json.str "sourceCommit")
                              Intact = manifest |> Option.bind (Json.bool "intact") }
                          Candidate = Json.str "candidate" json
                          RunId = Json.str "runId" json
                          Requirements = requirements
                          Declared = Json.prop "summary" json |> Option.bind parseDeclared
                          Acceptance =
                            { Accepted = acceptance |> Option.bind (Json.bool "accepted")
                              EvidenceProblems = acceptance |> Option.map (strings "evidenceProblems") |> Option.defaultValue []
                              Blockers = acceptance |> Option.map (strings "blockers") |> Option.defaultValue [] } })

    // ---- summary ----

    let isApplicable (r: Requirement) = r.Status <> Status.NotApplicable

    let summarize (requirements: Requirement list) : Summary =
        let applicable = requirements |> List.filter isApplicable
        let count status = requirements |> List.filter (fun r -> r.Status = status) |> List.length
        { Applicable = List.length applicable
          Implemented = applicable |> List.filter (fun r -> r.Implemented) |> List.length
          Verified = count Status.Pass
          Failed = count Status.Fail
          Blocked = count Status.Blocked
          NotRun = count Status.NotRun
          NotApplicable = count Status.NotApplicable }

    /// Complete only when there is something to verify and every applicable
    /// requirement passed.
    let isComplete (s: Summary) = s.Applicable > 0 && s.Verified = s.Applicable

    /// `part` of `whole` as a percentage with one decimal, rounded down, so a
    /// ratio below one is never shown as 100%. None when `whole` is zero.
    let percent (part: int) (whole: int) : string option =
        if whole <= 0 then None
        else
            let tenths = (int64 (max 0 (min part whole)) * 1000L) / int64 whole
            Some((float tenths / 10.0).ToString("0.0", Globalization.CultureInfo.InvariantCulture) + "%")

    /// Verified coverage: verified ÷ applicable (never implemented ÷ applicable).
    let verifiedPercent (s: Summary) = percent s.Verified s.Applicable

    /// Implementation coverage: implemented ÷ applicable. Not verification.
    let implementedPercent (s: Summary) = percent s.Implemented s.Applicable

    /// Fields where the file's own summary disagrees with its requirement list.
    let summaryMismatches (computed: Summary) (declared: Summary) : string list =
        [ "applicable", computed.Applicable, declared.Applicable
          "implemented", computed.Implemented, declared.Implemented
          "verified", computed.Verified, declared.Verified
          "failed", computed.Failed, declared.Failed
          "blocked", computed.Blocked, declared.Blocked
          "notRun", computed.NotRun, declared.NotRun
          "notApplicable", computed.NotApplicable, declared.NotApplicable ]
        |> List.filter (fun (_, c, d) -> c <> d)
        |> List.map (fun (name, c, d) -> $"{name} {d} declared, {c} listed")

    /// Applicable requirements that did not pass, failures first, each with
    /// the obligations that did not pass.
    let unverified (requirements: Requirement list) : (Requirement * Obligation list) list =
        let order (s: Status) = match s with Status.Fail -> 0 | Status.Blocked -> 1 | Status.NotRun -> 2 | _ -> 3
        requirements
        |> List.filter (fun r -> isApplicable r && r.Status <> Status.Pass)
        |> List.sortBy (fun r -> order r.Status, r.Id)
        |> List.map (fun r ->
            r, r.Obligations |> List.filter (fun o -> o.Status <> Status.Pass && o.Status <> Status.NotApplicable))

    // ---- filtering ----

    type Filter =
        { Screen: string option
          Type: string option
          Status: Status option }

    let noFilter = { Screen = None; Type = None; Status = None }

    let private allKey = "all"

    /// Parse a filter control value: "all" (or blank) clears the dimension.
    let private dimension (value: string) =
        match value.Trim() with
        | "" -> None
        | v when v = allKey -> None
        | v -> Some v

    let withScreen (value: string) (f: Filter) = { f with Screen = dimension value }
    let withType (value: string) (f: Filter) = { f with Type = dimension value }

    let withStatus (value: string) (f: Filter) =
        match dimension value with
        | None -> { f with Status = None }
        | Some code -> statusOfCode code |> Option.map (fun s -> { f with Status = Some s }) |> Option.defaultValue f

    let matches (f: Filter) (r: Requirement) =
        f.Screen |> Option.forall ((=) r.Screen)
        && f.Type |> Option.forall ((=) r.Type)
        && f.Status |> Option.forall ((=) r.Status)

    let apply (f: Filter) (requirements: Requirement list) = requirements |> List.filter (matches f)

    let isFiltered (f: Filter) = f <> noFilter

    /// Options for one filter control: (value, label, selected), "all" first.
    /// A selected value absent from the data is kept so the control never
    /// silently shows a different selection than the one applied.
    let options (allLabel: string) (selected: string option) (values: (string * string) list) : (string * string * bool) list =
        let present = values |> List.distinctBy fst |> List.sortBy fst
        let withSelected =
            match selected with
            | Some s when not (present |> List.exists (fun (v, _) -> v = s)) -> present @ [ s, s ]
            | _ -> present
        (allKey, allLabel, selected.IsNone) :: (withSelected |> List.map (fun (v, l) -> v, l, Some v = selected))

    let screenOptions (f: Filter) (requirements: Requirement list) =
        requirements |> List.map (fun r -> r.Screen, (if r.Screen = "" then "(no screen)" else r.Screen)) |> options "All screens" f.Screen

    let typeOptions (f: Filter) (requirements: Requirement list) =
        requirements |> List.map (fun r -> r.Type, (if r.Type = "" then "(no type)" else r.Type)) |> options "All types" f.Type

    let statusOptions (f: Filter) =
        (allKey, "All statuses", f.Status.IsNone)
        :: (allStatuses |> List.map (fun s -> statusCode s, statusLabel s, f.Status = Some s))
