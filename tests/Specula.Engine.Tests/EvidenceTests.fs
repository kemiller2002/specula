module Specula.Tests.EvidenceTests

open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

let adapter id = Evidence.registry |> List.find (fun a -> a.Id = id)

[<Fact>]
let ``parses causa.traceability/1 and maps statuses`` () =
    let text = """{"schema":"causa.traceability/1","scope":"P0","requirements":[
        {"id":"UI-1","title":"A","priority":"P0","status":"VERIFIED","evidence":"e2e"},
        {"id":"UI-2","title":"B","status":"PARTIAL"},
        {"id":"UI-3","title":"C","status":"BLOCKED"},
        {"id":"UI-4","title":"D","status":"NOT_ASSESSED"}]}"""
    match Evidence.parse (adapter "traceability") text with
    | Evidence.Parsed(Evidence.Requirements(Some "P0", rs)) ->
        Assert.Equal<EvidenceState list>([ Verified; Unverified; Blocked ], rs |> List.take 3 |> List.map (fun r -> r.State))
        match rs.[3].State with
        | Unavailable _ -> ()
        | other -> failwithf "unexpected %A" other
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``unknown schema versions are reported as unsupported, never guessed`` () =
    match Evidence.parse (adapter "traceability") """{"schema":"causa.traceability/2","requirements":[]}""" with
    | Evidence.Unsupported "causa.traceability/2" -> ()
    | other -> failwithf "unexpected %A" other
    match Evidence.parse (adapter "traceability") """{"requirements":[]}""" with
    | Evidence.Unsupported _ -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``malformed and oversized files are reported`` () =
    match Evidence.parse (adapter "traceability") "{not json" with
    | Evidence.Invalid _ -> ()
    | other -> failwithf "unexpected %A" other
    match Evidence.parse (adapter "traceability") (String.replicate (Evidence.maxBytes + 1) " ") with
    | Evidence.TooLarge _ -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``parses recorded kickoff`` () =
    match Evidence.parse (adapter "kickoff") """{"schema":"indy-init.rehearsal-kickoff/1","t0":"2026-10-29T22:49:55Z","deadline":"2026-10-30T05:49:55Z","timeLimitMinutes":420}""" with
    | Evidence.Parsed(Evidence.KickoffRecord k) ->
        Assert.Equal(t "2026-10-29T22:49:55Z", k.T0)
        Assert.Equal(Some 420L, k.LimitMinutes)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``reads decision and defect tables only under their exact headers`` () =
    let md = "# Decisions\n\n| ID | Decision | Why | Effect |\n|---|---|---|---|\n| KD-001 | Do x | Because | Then |\n| KD-002 | Do y | B | E |\n\nprose\n\n| Other | Table |\n|---|---|\n| a | b |\n"
    match Evidence.parse (adapter "decisions") md with
    | Evidence.Parsed(Evidence.Decisions ds) -> Assert.Equal<string list>([ "KD-001"; "KD-002" ], ds |> List.map (fun d -> d.Id))
    | other -> failwithf "unexpected %A" other
    match Evidence.parse (adapter "decisions") "# Nothing here" with
    | Evidence.Unsupported _ -> ()
    | other -> failwithf "unexpected %A" other
    let defects = "| ID | Defect | Found by | Fix | Guard |\n|---|---|---|---|---|\n| D-01 | bug | CI | fixed | test |\n"
    match Evidence.parse (adapter "defects") defects with
    | Evidence.Parsed(Evidence.Corrections [ c ]) -> Assert.Equal("D-01", c.Id)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``coverage distinguishes not found, partial and supported`` () =
    let source id outcome : Evidence.Source = { Adapter = adapter id; Sha = "x"; Outcome = outcome }
    Assert.Equal(Evidence.CoverageNotFound, Evidence.coverage [ source "traceability" Evidence.Missing; source "kickoff" Evidence.Missing ])
    Assert.Equal(Evidence.CoveragePartial, Evidence.coverage [ source "kickoff" (Evidence.Parsed(Evidence.Decisions [])); source "traceability" (Evidence.Unsupported "x") ])
    Assert.Equal(Evidence.CoverageSupported, Evidence.coverage [ source "traceability" (Evidence.Parsed(Evidence.Requirements(None, []))) ])
    Assert.NotEqual<string>("ok", Evidence.coverageLozenge Evidence.CoverageSupported)
