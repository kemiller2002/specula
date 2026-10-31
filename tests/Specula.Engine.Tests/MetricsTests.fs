module Specula.Tests.MetricsTests

open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

let inputs kickoff runs : Metrics.Inputs =
    { Kickoff = kickoff; Runs = runs; RunsTotal = Some(int64 (List.length runs)); Deployments = []; DeploymentStatuses = Map.empty; Requirements = None; Corrections = None; RunsUnreadable = None }

let runs =
    [ run 1L "ci" "a" Failed "2026-10-30T10:10:00Z" "2026-10-30T10:15:00Z" 1L
      run 2L "ci" "b" Passed "2026-10-30T10:30:00Z" "2026-10-30T10:40:00Z" 1L
      run 3L "ci" "c" Failed "2026-10-30T11:00:00Z" "2026-10-30T11:05:00Z" 1L
      run 4L "ci" "d" Passed "2026-10-30T11:30:00Z" "2026-10-30T11:35:00Z" 1L ]

[<Fact>]
let ``time to first passing CI requires a configured kickoff`` () =
    Assert.True((Metrics.timeToFirstPassingCi (inputs None runs)).Value.IsNone)
    let m = Metrics.timeToFirstPassingCi (inputs (Some(t "2026-10-30T10:00:00Z")) runs)
    Assert.Equal(Some "40m", m.Value)

[<Fact>]
let ``truncated run history makes the first-pass metric unmeasurable`` () =
    let i = { inputs (Some(t "2026-10-30T09:00:00Z")) runs with RunsTotal = Some 500L }
    Assert.True((Metrics.timeToFirstPassingCi i).Value.IsNone)

[<Fact>]
let ``failure to recovery pairs each failure with the next pass of the same workflow`` () =
    let cycles = Metrics.recoveries runs
    Assert.Equal(2, cycles.Length)
    Assert.Equal<System.TimeSpan list>([ System.TimeSpan.FromMinutes 25.0; System.TimeSpan.FromMinutes 30.0 ], cycles |> List.map (fun (_, _, s) -> s))

[<Fact>]
let ``missing evidence yields not measurable, never zero`` () =
    let m = Metrics.verifiedRequirementsPerHour (inputs (Some(t "2026-10-30T10:00:00Z")) runs)
    Assert.True(m.Value.IsNone)
    let empty = Metrics.failureToRecovery (inputs None [])
    Assert.True(empty.Value.IsNone)
    Assert.True((Metrics.timeToFirstDeployment (inputs (Some(t "2026-10-30T10:00:00Z")) [])).Value.IsNone)

[<Fact>]
let ``every metric declares definition, denominator, window and provenance`` () =
    Metrics.all (inputs None runs)
    |> List.iter (fun m ->
        Assert.False(System.String.IsNullOrWhiteSpace m.Definition)
        Assert.False(System.String.IsNullOrWhiteSpace m.Denominator)
        Assert.False(System.String.IsNullOrWhiteSpace m.Window)
        Assert.False(System.String.IsNullOrWhiteSpace m.Provenance))

[<Fact>]
let ``competition clock reports elapsed, remaining and deadline passed`` () =
    let c = { Preferences.indyInitPreset with Kickoff = Some(t "2026-10-30T10:00:00Z") }
    let mid = (Metrics.clock (t "2026-10-30T11:00:00Z") c).Value
    Assert.Equal(System.TimeSpan.FromHours 1.0, mid.Elapsed)
    Assert.Equal(System.TimeSpan.FromMinutes 360.0, mid.Remaining)
    Assert.True((Metrics.clock (t "2026-10-30T18:00:00Z") c).Value.Passed)
    Assert.True((Metrics.clock (t "2026-10-30T11:00:00Z") Preferences.indyInitPreset).IsNone)

[<Fact>]
let ``unreadable workflow runs make run-based metrics unmeasurable instead of "none observed"`` () =
    let i = { inputs (Some(t "2026-10-30T10:00:00Z")) [] with RunsUnreadable = Some "rate limited" }
    let ms = Metrics.all i
    for id in [ "time-to-first-passing-ci"; "failure-to-recovery"; "correction-cycles" ] do
        let m = ms |> List.find (fun m -> m.Id = id)
        Assert.True(m.Value.IsNone, id)
        Assert.Contains("could not be read", m.Reason)
