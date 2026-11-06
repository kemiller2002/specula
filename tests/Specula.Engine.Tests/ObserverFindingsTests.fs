module Specula.Tests.ObserverFindingsTests

open System.IO
open Xunit
open Specula.Engine

/// Real `requirements-compiler observe` output: the Indy-init Rehearsal 010 readiness replay of Rehearsal 009
/// (kemiller2002/Indy-init docs/rehearsal-010-readiness/evidence/r9-replay/observer-findings.json).
let private replay () =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "observer-findings-r9-replay.json"))

let private adapter = Evidence.registry |> List.find (fun a -> a.Id = "observer-findings")

let private invalid (text: string) =
    match Evidence.parse adapter text with
    | Evidence.Invalid _ -> true
    | _ -> false

[<Fact>]
let ``the observer adapter reads only the allowlisted path with its exact schema`` () =
    Assert.Equal("evidence/observer/findings.json", adapter.Path)
    Assert.Equal("indy-init.observer-findings/1", adapter.Schema)

[<Fact>]
let ``real observer findings are counted by Specula, with stop-the-line rules kept`` () =
    match Evidence.parse adapter (replay ()) with
    | Evidence.Parsed(Evidence.ObserverFindingsResult r) ->
        Assert.Equal(10, r.Blocking)
        Assert.Equal(1, r.Warnings)
        Assert.Contains(r.Findings, fun f -> f.Code = "PREMATURE_STOP" && f.Severity = ObserverFindings.Blocking)
        Assert.Contains(r.Findings, fun f -> f.Code = "SELF_VERIFIED_SUCCESS" && f.StopTheLine = Some "verification collapsed into implementation")
        Assert.True(r.Deadline.IsSome)
    | other -> failwithf "expected parsed findings, got %A" other

[<Fact>]
let ``a file whose summary disagrees with its findings is unreadable, not believed`` () =
    Assert.True(invalid ((replay ()).Replace("\"blocking\": 10,", "\"blocking\": 0,")))

[<Fact>]
let ``an unknown severity makes the whole file unreadable`` () =
    Assert.True(invalid ((replay ()).Replace("\"severity\": \"warning\"", "\"severity\": \"info\"")))

[<Fact>]
let ``an observer that does not declare itself read-only is refused`` () =
    Assert.True(invalid ((replay ()).Replace("\"readOnly\": true", "\"readOnly\": false")))

[<Fact>]
let ``another schema is unsupported, never parsed`` () =
    match Evidence.parse adapter ((replay ()).Replace("indy-init.observer-findings/1", "indy-init.observer-findings/2")) with
    | Evidence.Unsupported _ -> ()
    | other -> failwithf "expected Unsupported, got %A" other
