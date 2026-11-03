module Specula.Tests.RequirementVerificationTests

open System
open System.IO
open Xunit
open Limen.Contract.Core
open Specula.Engine
open Specula.Engine.Model
open Specula.Engine.Update
open Specula.Tests.Fixtures
open Specula.Tests.UpdateTests

module RV = RequirementVerification

/// Real dokimos.requirement-verification/1 output from the verification pipeline.
let sample =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "requirement-verification.json"))

let adapter = Evidence.registry |> List.find (fun a -> a.Kind = Evidence.RequirementVerificationReport)

let parsed (text: string) =
    match Evidence.parse adapter text with
    | Evidence.Parsed(Evidence.RequirementVerificationResult report) -> report
    | other -> failwithf "unexpected %A" other

let report = lazy (parsed sample)

let ids (rs: RV.Requirement list) = rs |> List.map (fun r -> r.Id)

let requirement id status implemented : RV.Requirement =
    { Id = id; Type = "ui-field"; Screen = "s"; Name = id; Required = true; Status = status
      FailureClass = None; Implemented = implemented; Obligations = [] }

// ---- parsing ----

[<Fact>]
let ``the registry reads the report from its allowlisted path and schema`` () =
    Assert.Equal("evidence/requirement-verification/requirement-verification.json", adapter.Path)
    Assert.Equal("dokimos.requirement-verification/1", adapter.Schema)

[<Fact>]
let ``parses the real sample report`` () =
    let r = report.Value
    Assert.Equal(18, List.length r.Requirements)
    Assert.Equal(Some "dd9a2a75b762891764ccc7fdf6fc7e7efe02135361e01d983ba8d9a8aa681c0c", r.Manifest.ContentHash)
    Assert.Equal(Some "d6f5cb53ff3d43a158aad52a56ec696eba44dcb2", r.Manifest.SourceCommit)
    Assert.Equal(Some "CAT-CAUSA-INVESTIGATION", r.Manifest.CatalogId)
    Assert.Equal(Some true, r.Manifest.Intact)
    Assert.Equal(Some "local-3", r.RunId)
    Assert.Equal(Some false, r.Acceptance.Accepted)
    Assert.Equal(18, List.length r.Acceptance.Blockers)
    let first = r.Requirements |> List.head
    Assert.Equal("INV-001", first.Id)
    Assert.Equal("Problem statement", first.Name)
    Assert.Equal("ui-field", first.Type)
    Assert.Equal("investigation-start", first.Screen)
    Assert.Equal(RV.Status.Fail, first.Status)
    Assert.Equal(Some "lifecycle", first.FailureClass)
    Assert.True(first.Implemented)
    let passing = r.Requirements |> List.find (fun x -> x.Status = RV.Status.Pass)
    Assert.Equal(None, passing.FailureClass)

[<Fact>]
let ``a different or missing schema is unsupported, never guessed`` () =
    match Evidence.parse adapter (sample.Replace("dokimos.requirement-verification/1", "dokimos.requirement-verification/2")) with
    | Evidence.Unsupported "dokimos.requirement-verification/2" -> ()
    | other -> failwithf "unexpected %A" other
    match Evidence.parse adapter """{"schema":"causa.traceability/1","requirements":[]}""" with
    | Evidence.Unsupported "causa.traceability/1" -> ()
    | other -> failwithf "unexpected %A" other
    match Evidence.parse adapter """{"requirements":[]}""" with
    | Evidence.Unsupported _ -> ()
    | other -> failwithf "unexpected %A" other

[<Theory>]
[<InlineData("{not json")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1"}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":{}}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":[{"status":"PASS","implemented":true}]}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":[{"id":"R-1","status":"MAYBE","implemented":true}]}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":[{"id":"R-1","status":"PASS"}]}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":[{"id":"R-1","status":"PASS","implemented":true,"obligations":[{"id":"R-1/V1/x","status":"WHATEVER"}]}]}""")>]
[<InlineData("""{"schema":"dokimos.requirement-verification/1","requirements":[{"id":"R-1","status":"PASS","implemented":true},{"id":"R-1","status":"FAIL","implemented":true}]}""")>]
let ``malformed reports are rejected as invalid`` (text: string) =
    match Evidence.parse adapter text with
    | Evidence.Invalid _ -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``an oversized report is refused`` () =
    match Evidence.parse adapter (String.replicate (Evidence.maxBytes + 1) " ") with
    | Evidence.TooLarge _ -> ()
    | other -> failwithf "unexpected %A" other

// ---- summary ----

[<Fact>]
let ``summary numbers are computed from the sample's requirement list`` () =
    let r = report.Value
    let s = RV.summarize r.Requirements
    let expected : RV.Summary = { Applicable = 17; Implemented = 14; Verified = 10; Failed = 7; Blocked = 0; NotRun = 0; NotApplicable = 1 }
    Assert.Equal(expected, s)
    Assert.Equal(Some "58.8%", RV.verifiedPercent s)
    Assert.Equal(Some "82.3%", RV.implementedPercent s)
    Assert.False(RV.isComplete s)
    Assert.Equal<string list>([], RV.summaryMismatches s r.Declared.Value)

[<Fact>]
let ``failing requirement IDs come with their failing obligation IDs`` () =
    let unverified = RV.unverified report.Value.Requirements
    Assert.Equal<string list>([ "INV-001"; "INV-002"; "INV-007"; "INV-008"; "INV-011"; "INV-012"; "INV-016" ], unverified |> List.map (fun (r, _) -> r.Id))
    let obligationsOf id = unverified |> List.find (fun (r, _) -> r.Id = id) |> snd |> List.map (fun o -> o.Id)
    Assert.Equal<string list>([ "INV-001/V5/save" ], obligationsOf "INV-001")
    Assert.Equal<string list>([ "INV-016/V2/accessible" ], obligationsOf "INV-016")
    Assert.Contains("INV-011/V5/save", obligationsOf "INV-011")
    let (_, inv016) = unverified |> List.find (fun (r, _) -> r.Id = "INV-016")
    Assert.Equal("<span> carrying INV-016 exposes no semantic role", inv016.Head.Reason)

[<Fact>]
let ``a declared summary that disagrees with the list is reported`` () =
    let s = RV.summarize report.Value.Requirements
    let mismatches = RV.summaryMismatches s { s with Verified = 17 }
    Assert.Equal<string list>([ "verified 17 declared, 10 listed" ], mismatches)

// ---- never 100% unless every applicable requirement passes ----

[<Fact>]
let ``coverage is never 100 percent unless every applicable requirement passes`` () =
    let almost = [ for i in 1..999 -> requirement $"R-{i}" RV.Status.Pass true ] @ [ requirement "R-1000" RV.Status.Fail true ]
    let s = RV.summarize almost
    Assert.Equal(Some "99.9%", RV.verifiedPercent s)
    Assert.False(RV.isComplete s)
    // Implementation coverage can be 100% while verification is not.
    Assert.Equal(Some "100.0%", RV.implementedPercent s)
    let notRun = RV.summarize [ requirement "R-1" RV.Status.Pass true; requirement "R-2" RV.Status.NotRun true ]
    Assert.False(RV.isComplete notRun)
    Assert.Equal(Some "50.0%", RV.verifiedPercent notRun)
    let all = RV.summarize [ requirement "R-1" RV.Status.Pass true; requirement "R-2" RV.Status.Pass true; requirement "R-3" RV.Status.NotApplicable false ]
    Assert.True(RV.isComplete all)
    Assert.Equal(Some "100.0%", RV.verifiedPercent all)
    let none = RV.summarize [ requirement "R-1" RV.Status.NotApplicable false ]
    Assert.False(RV.isComplete none)
    Assert.Equal(None, RV.verifiedPercent none)

[<Fact>]
let ``verified coverage uses verified, not implemented, over applicable`` () =
    let s = RV.summarize [ requirement "R-1" RV.Status.Fail true; requirement "R-2" RV.Status.Fail true; requirement "R-3" RV.Status.Pass true; requirement "R-4" RV.Status.Blocked false ]
    Assert.Equal(Some "25.0%", RV.verifiedPercent s)
    Assert.Equal(Some "75.0%", RV.implementedPercent s)

// ---- filtering ----

[<Fact>]
let ``filters by screen`` () =
    let f = RV.noFilter |> RV.withScreen "investigation-start"
    let shown = RV.apply f report.Value.Requirements
    Assert.Equal(6, List.length shown)
    Assert.True(shown |> List.forall (fun r -> r.Screen = "investigation-start"))

[<Fact>]
let ``filters by requirement type`` () =
    let shown = RV.apply (RV.noFilter |> RV.withType "ui-field") report.Value.Requirements
    Assert.Equal(5, List.length shown)
    Assert.True(shown |> List.forall (fun r -> r.Type = "ui-field"))

[<Fact>]
let ``filters by verification status`` () =
    let all = report.Value.Requirements
    Assert.Equal<string list>([ "INV-001"; "INV-002"; "INV-007"; "INV-008"; "INV-011"; "INV-012"; "INV-016" ], RV.apply (RV.noFilter |> RV.withStatus "FAIL") all |> ids)
    Assert.Equal<string list>([ "INV-003" ], RV.apply (RV.noFilter |> RV.withStatus "NOT_APPLICABLE") all |> ids)
    Assert.Equal(10, RV.apply (RV.noFilter |> RV.withStatus "PASS") all |> List.length)
    Assert.Empty(RV.apply (RV.noFilter |> RV.withStatus "BLOCKED") all)

[<Fact>]
let ``filters combine, and all clears one dimension`` () =
    let all = report.Value.Requirements
    let f = RV.noFilter |> RV.withScreen "investigation-workspace" |> RV.withStatus "FAIL"
    let shown = RV.apply f all
    Assert.True(shown |> List.forall (fun r -> r.Screen = "investigation-workspace" && r.Status = RV.Status.Fail))
    Assert.Equal<string list>(RV.apply (RV.noFilter |> RV.withStatus "FAIL") all |> List.filter (fun r -> r.Screen = "investigation-workspace") |> ids, ids shown)
    let cleared = f |> RV.withScreen "all" |> RV.withStatus "all"
    Assert.Equal(RV.noFilter, cleared)
    Assert.Equal(18, RV.apply cleared all |> List.length)
    // An unrecognized status value leaves the filter as it was.
    Assert.Equal(f, f |> RV.withStatus "SOMETIMES")

[<Fact>]
let ``filter options list each value once with the selection marked`` () =
    let all = report.Value.Requirements
    let f = RV.noFilter |> RV.withScreen "investigation-start"
    Assert.Equal<(string * string * bool) list>(
        [ "all", "All screens", false; "investigation-start", "investigation-start", true; "investigation-workspace", "investigation-workspace", false ],
        RV.screenOptions f all)
    let statuses = RV.statusOptions RV.noFilter
    Assert.Equal(6, List.length statuses)
    Assert.Equal(("all", "All statuses", true), List.head statuses)

// ---- through the engine and the view ----

let private withEvidence (text: string) (url: string) =
    if url.Contains "raw.githubusercontent.com" && url.EndsWith RV.path then UpdateTests.text text else github url

let private loaded host =
    settle host (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))

let private textOf (view: ViewState) key =
    match view.[key] with
    | ViewValue.Text s -> s
    | other -> failwithf "%s is %A" key other

let private rows (view: ViewState) key =
    match view.[key] with
    | ViewValue.Items xs -> xs
    | other -> failwithf "%s is %A" key other

let private event name value : SemanticEvent =
    { Name = name; Key = None; Value = Some value; Checked = None; Values = None; Submitter = None }

[<Fact>]
let ``without the report the view says none was found, not zero`` () =
    let view = View.project (loaded github)
    Assert.Equal(ViewValue.Flag false, view.["hasRv"])
    Assert.Equal(ViewValue.Flag true, view.["noRv"])
    Assert.Contains("No requirement verification evidence was found", textOf view "rvUnavailable")
    Assert.Contains("unknown, not zero", textOf view "rvUnavailable")
    Assert.Equal("Unknown", textOf view "rvApplicable")
    Assert.Equal("Unknown", textOf view "rvVerified")
    Assert.Empty(rows view "rvRows")

[<Fact>]
let ``an unsupported report says so`` () =
    let view = View.project (loaded (withEvidence """{"schema":"dokimos.requirement-verification/9","requirements":[]}"""))
    Assert.Equal(ViewValue.Flag true, view.["noRv"])
    Assert.Contains("unsupported schema (dokimos.requirement-verification/9)", textOf view "rvUnavailable")

[<Fact>]
let ``the sample projects counts, provenance and failing IDs`` () =
    let model = loaded (withEvidence sample)
    let view = View.project model
    Assert.Equal(ViewValue.Flag true, view.["hasRv"])
    Assert.Equal("17", textOf view "rvApplicable")
    Assert.Equal("14", textOf view "rvImplemented")
    Assert.Equal("10", textOf view "rvVerified")
    Assert.Equal("7", textOf view "rvFailed")
    Assert.Equal("0", textOf view "rvBlocked")
    Assert.Equal("0", textOf view "rvNotRun")
    Assert.Equal("58.8%", textOf view "rvVerifiedPercent")
    Assert.Equal("82.3%", textOf view "rvImplementedPercent")
    Assert.StartsWith("Not complete", textOf view "rvHeadline")
    Assert.Equal("d6f5cb53ff3d43a158aad52a56ec696eba44dcb2", textOf view "rvSourceCommit")
    Assert.Equal("dd9a2a75b762891764ccc7fdf6fc7e7efe02135361e01d983ba8d9a8aa681c0c", textOf view "rvManifestHash")
    Assert.StartsWith("Not passed", textOf view "rvAccepted")
    // HEAD's commit time from the loaded history (2026-10-30T11:00:00Z).
    Assert.Contains("2026", textOf view "rvTime")
    Assert.Contains($"commit time of {Repo.shortSha headSha}", textOf view "rvTime")
    Assert.Equal(ViewValue.Flag false, view.["hasRvMismatch"])
    Assert.Equal<string list>(
        [ "INV-001"; "INV-002"; "INV-007"; "INV-008"; "INV-011"; "INV-012"; "INV-016" ],
        rows view "rvUnverified" |> List.map (fun i -> match i.["id"] with ViewPrimitive.Text s -> s | _ -> ""))
    Assert.Equal(18, rows view "rvRows" |> List.length)

[<Fact>]
let ``verification time is unavailable when the HEAD commit time is not loaded`` () =
    let model = loaded (withEvidence sample)
    let snapshot = Model.currentSnapshot model |> Option.map (fun s -> { s with Commits = [] })
    Assert.Equal("Time unavailable", View.requirementVerificationTime model snapshot headSha)

[<Fact>]
let ``filter events from the select controls narrow the requirement list`` () =
    let model = loaded (withEvidence sample)
    let filtered = Update.update (Update.ofEvent (event "reqStatus" "FAIL")) model
    let view = View.project filtered.Model
    Assert.Equal(7, rows view "rvRows" |> List.length)
    Assert.Equal("FAIL", textOf view "rvStatusValue")
    Assert.Contains("Showing 7 of 18", textOf view "rvShown")
    let narrowed = Update.update (Update.ofEvent (event "reqScreen" "investigation-start")) filtered.Model
    let narrowedRows = rows (View.project narrowed.Model) "rvRows"
    Assert.Equal(2, List.length narrowedRows)
    let typed = Update.update (Update.ofEvent (event "reqType" "action")) (Update.update (Update.ofEvent (event "reqStatus" "all")) model).Model
    Assert.True(rows (View.project typed.Model) "rvRows" |> List.forall (fun i -> i.["type"] = ViewPrimitive.Text "action"))
    // The failing list is the full list regardless of filters.
    Assert.Equal(7, rows (View.project narrowed.Model) "rvUnverified" |> List.length)

[<Fact>]
let ``the requirements route is a known, remembered screen`` () =
    Assert.Equal(Some Requirements, Model.screenOfKey "requirements")
    Assert.Equal("requirements", Model.screenKey Requirements)
    Assert.Contains("requirements", Preferences.knownViews)
