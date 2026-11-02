module Specula.Tests.OrdoDecisionsTests

open System
open System.IO
open Xunit
open Limen.Contract.Core
open Specula.Engine

/// Real `sde ecir recognize --events` output from kemiller2002/ordo
/// docs/observability/acceptance (timer fixture cohort, not a rehearsal).
let private acceptanceLog () =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "ordo-decision-events.jsonl"))

let private adapter = Evidence.registry |> List.find (fun a -> a.Id = "ordo-decisions")

let private source outcome : Evidence.Source = { Adapter = adapter; Sha = "abc1234def"; Outcome = outcome }

let private text (entries: (string * ViewValue) list) key =
    match entries |> List.tryFind (fst >> (=) key) with
    | Some(_, ViewValue.Text v) -> v
    | other -> failwithf "expected text for %s, got %A" key other

[<Fact>]
let ``the ordo adapter reads only the allowlisted path with its exact schema`` () =
    Assert.Equal("evidence/ordo/decision-events.jsonl", adapter.Path)
    Assert.Equal("ordo.decision-event/1", adapter.Schema)

[<Fact>]
let ``a real decision event log is counted, with execution and verification kept separate`` () =
    match Evidence.parse adapter (acceptanceLog ()) with
    | Evidence.Parsed(Evidence.OrdoDecisionEvents s) ->
        Assert.Equal(2, s.Runs)
        Assert.Equal(0, s.IncompleteRuns)
        Assert.Equal(12, s.Evaluated)
        Assert.Equal(3, s.Matched)
        Assert.Equal(Some 0.25, s.MatchRate)
        Assert.Equal(4, s.FallbackToAgent)
        Assert.Equal<(string * int) list>([ "succeeded", 1 ], s.ExecutionsReported)
        Assert.Equal<(string * int) list>([ "failed", 1 ], s.Verified)
        // Seven decisions were selected or delegated; one execution was reported.
        Assert.Equal(6, s.ExecutionUnknown)
        Assert.Equal(0, s.ExecutedButUnverified)
        Assert.Contains(("state.machine-guarded-transition", 3), s.ByFamily)
        Assert.Equal(0, s.CorruptLines)
    | other -> failwithf "expected parsed decision events, got %A" other

[<Fact>]
let ``damaged lines are counted as corrupt and a truncated run as incomplete`` () =
    let lines = (acceptanceLog ()).Split('\n') |> Array.filter (String.IsNullOrWhiteSpace >> not)
    let damaged = Array.append [| lines.[0]; lines.[1].Substring(0, 40); "{\"schemaVersion\":\"ordo.decision-event/9\"}" |] (Array.skip 2 lines)
    match Evidence.parse adapter (String.Join("\n", damaged)) with
    | Evidence.Parsed(Evidence.OrdoDecisionEvents s) ->
        Assert.Equal(1, s.CorruptLines)
        Assert.Equal(1, s.UnsupportedLines)
        Assert.Equal(1, s.IncompleteRuns)
        Assert.Equal(11, s.Evaluated)
    | other -> failwithf "expected parsed, got %A" other

[<Fact>]
let ``a file with no readable event is reported, not shown as zero decisions`` () =
    match Evidence.parse adapter "not json\n{also not}" with
    | Evidence.Invalid _ -> ()
    | other -> failwithf "expected Invalid, got %A" other
    match Evidence.parse adapter "{\"schemaVersion\":\"other/1\"}" with
    | Evidence.Unsupported _ -> ()
    | other -> failwithf "expected Unsupported, got %A" other

[<Fact>]
let ``a log with runs but no evaluations has no match rate`` () =
    let s = OrdoDecisions.summarize ""
    Assert.Equal(None, s.MatchRate)

[<Fact>]
let ``missing evidence is shown as unknown, never as zero`` () =
    let entries = View.ordoEntries None (Some "abc1234def") [ source Evidence.Missing ]
    Assert.Equal("Unknown", text entries "ordoEvaluated")
    Assert.Equal("Unknown", text entries "ordoMatchRate")
    Assert.Contains("unknown, not zero", text entries "ordoUnavailable")

[<Fact>]
let ``the dashboard projection shows real counts and the audit snapshot is labeled as such`` () =
    let summary =
        match Evidence.parse adapter (acceptanceLog ()) with
        | Evidence.Parsed p -> p
        | other -> failwithf "%A" other
    let entries = View.ordoEntries None (Some "abc1234def") [ source (Evidence.Parsed summary) ]
    Assert.Equal("12", text entries "ordoEvaluated")
    Assert.Equal("25.0%", text entries "ordoMatchRate")
    Assert.Equal("4", text entries "ordoFallback")
    Assert.Contains("not live data", text entries "ordoAuditCaption")
    Assert.Equal(6, OrdoDecisions.rehearsalAudit.Length)
    Assert.All(OrdoDecisions.rehearsalAudit, fun r -> Assert.Equal("Unknown", r.PatternRecognition))
