module Specula.Tests.StatusTests

open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

let head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
let old = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"

[<Fact>]
let ``old green run never verifies a new HEAD`` () =
    let runs = [ run 1L "ci" old Passed "2026-10-30T09:00:00Z" "2026-10-30T09:05:00Z" 1L ]
    match Status.headVerdict head runs [] with
    | Status.HeadUnknown _ -> ()
    | other -> failwithf "expected unknown, got %A" other
    Assert.True((Status.olderPassingRun head runs).IsSome)

[<Fact>]
let ``failing check on HEAD wins over passing ones`` () =
    let checks = [ check 1L "build" head Passed; check 2L "test" head Failed ]
    Assert.Equal(Status.HeadFailing, Status.headVerdict head [] checks)

[<Fact>]
let ``running check makes HEAD running, all success makes it passing`` () =
    Assert.Equal(Status.HeadRunning, Status.headVerdict head [] [ check 1L "a" head Passed; check 2L "b" head Running ])
    Assert.Equal(Status.HeadPassing, Status.headVerdict head [] [ check 1L "a" head Passed; check 2L "b" head Skipped ])

[<Fact>]
let ``a rerun supersedes the failed attempt of the same workflow`` () =
    let runs =
        [ run 1L "ci" head Failed "2026-10-30T09:00:00Z" "2026-10-30T09:05:00Z" 1L
          run 1L "ci" head Passed "2026-10-30T09:10:00Z" "2026-10-30T09:15:00Z" 2L ]
    Assert.Equal(Status.HeadPassing, Status.headVerdict head runs [])

[<Fact>]
let ``sha comparison is case-insensitive and exact`` () =
    let checks = [ check 1L "a" (head.ToUpperInvariant()) Passed ]
    Assert.Equal(Status.HeadPassing, Status.headVerdict head [] checks)
    match Status.headVerdict (head.Substring(0, 7)) [] checks with
    | Status.HeadUnknown _ -> ()
    | other -> failwithf "prefix must not match: %A" other

[<Fact>]
let ``deployment without smoke evidence is not verified`` () =
    let deployment : Deployment = { Id = 9L; Sha = head; Ref = Some "main"; Environment = "github-pages"; Creator = None; CreatedAt = Some(t "2026-10-30T10:00:00Z"); StatusesUrl = None }
    let status : DeploymentStatus = { Id = 1L; State = DeploySuccess; CreatedAt = Some(t "2026-10-30T10:01:00Z"); EnvironmentUrl = Some "https://example.org/"; LogUrl = None }
    let rows = Status.truthRows (Some head) Status.HeadPassing [] [ check 1L "build" head Passed ] [ deployment ] (Map.ofList [ 9L, [ status ] ])
    let smoke = rows |> List.find (fun r -> r.Id = "smoke")
    Assert.Equal("Not verified", smoke.Value)
    Assert.NotEqual<string>("ok", smoke.State)
    let url = rows |> List.find (fun r -> r.Id = "deploy-url")
    Assert.Equal("Recorded", url.Value)
    Assert.NotEqual<string>("ok", url.State)

[<Fact>]
let ``unknown is never rendered with the success state`` () =
    Assert.Equal("unknown", Status.verdictState (Status.HeadUnknown "x"))
    Assert.Equal("unknown", Status.outcomeState UnknownOutcome)
    Assert.Equal("ok", Status.outcomeState Passed)

[<Fact>]
let ``a deploy job on HEAD counts as the deployment workflow, a smoke job does not`` () =
    let checks = [ check 1L "build-and-test" head Passed; check 2L "deploy" head Passed; check 3L "deployed-smoke" head Passed ]
    let rows = Status.truthRows (Some head) Status.HeadPassing [] checks [] Map.empty
    let deploy = rows |> List.find (fun r -> r.Id = "deploy-workflow")
    Assert.Equal("Passing", deploy.Value)
    Assert.Contains("job name: deploy", deploy.Detail)
    let none = Status.truthRows (Some head) Status.HeadPassing [] [ check 1L "build" head Passed; check 3L "deployed-smoke" head Passed ] [] Map.empty
    Assert.Equal("Not found", (none |> List.find (fun r -> r.Id = "deploy-workflow")).Value)
