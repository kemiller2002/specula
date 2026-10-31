module Specula.Tests.UpdateTests

open System
open Xunit
open Limen.Contract
open Limen.Contract.Core
open Specula.Engine
open Specula.Engine.Model
open Specula.Engine.Update
open Specula.Tests.Fixtures

let now = t "2026-10-30T12:00:00Z"
let headSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
let oldSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
let location = { Origin = "https://specula.test"; Path = "/"; Query = ""; Hash = "" }

let ok (body: string) = EffectOutcome.Success(200L, RawJson body, Some(Map.ofList [ "x-ratelimit-limit", "60"; "x-ratelimit-remaining", "50" ]))
let status code (body: string) = EffectOutcome.Success(code, RawJson body, None)
let text (body: string) = EffectOutcome.Success(200L, RawJson(Json.quote body), None)

let repoJson (owner: string) (name: string) =
    $"""{{"name":"{name}","owner":{{"login":"{owner}"}},"default_branch":"main","html_url":"https://github.com/{owner}/{name}","private":false,"created_at":"2026-10-01T00:00:00Z"}}"""

/// Standard fixture GitHub: HEAD has a failing check, an older commit passed.
let github (url: string) : EffectOutcome =
    match url with
    | u when u.Contains "raw.githubusercontent.com" -> status 404L "\"404: Not Found\""
    | u when u.EndsWith "/repos/octo/demo" -> ok (repoJson "octo" "demo")
    | u when u.EndsWith "/repos/octo/other" -> ok (repoJson "octo" "other")
    | u when u.Contains "/commits?" ->
        ok ("[" + commitJson headSha "head" "2026-10-30T11:00:00Z" + "," + commitJson oldSha "older" "2026-10-30T10:00:00Z" + "]")
    | u when u.Contains "/pulls?" -> ok "[]"
    | u when u.Contains "/actions/runs" ->
        ok $"""{{"total_count":1,"workflow_runs":[{{"id":7,"name":"ci","head_sha":"{oldSha}","status":"completed","conclusion":"success","run_attempt":1,"created_at":"2026-10-30T10:01:00Z","updated_at":"2026-10-30T10:05:00Z"}}]}}"""
    | u when u.Contains "/check-runs" ->
        ok $"""{{"total_count":1,"check_runs":[{{"id":8,"name":"build","head_sha":"{headSha}","status":"completed","conclusion":"failure"}}]}}"""
    | u when u.Contains "/deployments" -> ok "[]"
    | u when u.Contains "/branches/" -> ok $"""{{"commit":{{"sha":"{headSha}"}}}}"""
    | _ -> status 404L "{}"

let httpEffects (step: Step) =
    step.Effects |> List.choose (function EffectRequest.Http h -> Some h | _ -> None)

/// Answer every HTTP effect (and those it triggers) with the fake GitHub.
let rec settle (host: string -> EffectOutcome) (step: Step) : Model =
    match httpEffects step with
    | [] -> step.Model
    | requests ->
        requests
        |> List.fold
            (fun (model, effects) (h: HttpEffectRequest) ->
                let (CorrelationId id) = h.CorrelationId
                let next = Update.update (HttpDone(id, host h.Url)) model
                next.Model, effects @ next.Effects)
            (step.Model, [])
        |> fun (model, effects) -> settle host { Model = model; Effects = effects; Cancellations = [] }

let storageId (step: Step) =
    step.Effects
    |> List.pick (function
        | EffectRequest.Storage(StorageEffectRequest.Get(CorrelationId id, _)) -> Some id
        | _ -> None)

let boot (stored: StorageOutcome) =
    let start = Update.update (Init location) (Model.initial now TimeZoneInfo.Utc)
    Update.update (StorageDone(storageId start, stored)) start.Model

let select (repoText: string) (model: Model) =
    let typed = Update.update (DraftChanged repoText) model
    Update.update Observe typed.Model

let savedPrefs (step: Step) =
    step.Effects
    |> List.tryPick (function
        | EffectRequest.Storage(StorageEffectRequest.Set(_, _, value)) -> Some value
        | _ -> None)

[<Fact>]
let ``first selection persists, navigates to overview and loads data`` () =
    let booted = boot (StorageOutcome.Success None)
    Assert.Equal(Picker, booted.Model.Screen)
    let selecting = select "https://github.com/octo/demo" booted.Model
    let afterRepo =
        let h = httpEffects selecting |> List.exactlyOne
        let (CorrelationId id) = h.CorrelationId
        Update.update (HttpDone(id, github h.Url)) selecting.Model
    Assert.Contains("octo/demo", (savedPrefs afterRepo).Value)
    Assert.Equal(Overview, afterRepo.Model.Screen)
    let model = settle github afterRepo
    match model.Fetch with
    | Fresh _ -> ()
    | other -> failwithf "expected fresh, got %A" other

[<Fact>]
let ``old green CI with a failing HEAD never reports HEAD passing`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let model = settle github booted
    let view = View.project model
    Assert.Equal(ViewValue.Text "Failing", view.["headLabel"])
    Assert.Equal(ViewValue.Flag true, view.["hasOlderPass"])

[<Fact>]
let ``reload restores the saved repository and refreshes automatically`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo","selectedView":"activity"}"""))
    Assert.Equal(Some repo, booted.Model.Selection)
    Assert.Equal(Activity, booted.Model.Screen)
    Assert.Equal(Loading, booted.Model.Fetch)
    Assert.Single(httpEffects booted) |> ignore

[<Fact>]
let ``repeated refresh while loading is single-flight`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let again = [ 1 .. 10 ] |> List.fold (fun (m: Model) _ -> (Update.update Refresh m).Model) booted.Model
    Assert.Equal(1, again.Pending |> Map.filter (fun _ p -> p.Url.IsSome) |> Map.count)
    let model = settle github { booted with Model = again }
    let repeated = [ 1 .. 10 ] |> List.fold (fun m _ -> settle github (Update.update Refresh m)) model
    Assert.Equal(Some repo, repeated.Selection)
    let events = Model.events (Model.currentSnapshot repeated).Value
    Assert.Equal(events.Length, (events |> List.distinctBy (fun e -> e.StableId)).Length)

[<Fact>]
let ``switching repository during a fetch never shows the previous repository's data`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let repoA = httpEffects booted |> List.exactlyOne
    let switching = select "octo/other" booted.Model
    Assert.Contains(repoA.CorrelationId, switching.Cancellations)
    // The late response for repository A arrives after the switch.
    let (CorrelationId idA) = repoA.CorrelationId
    let late = Update.update (HttpDone(idA, github repoA.Url)) switching.Model
    Assert.True(late.Effects.IsEmpty)
    let model = settle github { switching with Model = late.Model }
    Assert.Equal(Some { Owner = "octo"; Name = "other" }, model.Selection)
    Assert.True(Model.currentSnapshot model |> Option.forall (fun s -> s.Repo.Name = "other"))

[<Fact>]
let ``a failed switch keeps the saved repository`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let model = settle github booted
    let switching = select "octo/missing" model
    let final = settle github switching
    Assert.Equal(Some repo, final.Selection)
    Assert.True(final.DraftError.IsSome)

[<Fact>]
let ``unavailable or corrupt storage keeps the app usable`` () =
    let blocked = boot (StorageOutcome.Failure StorageFailureReason.Unavailable)
    match blocked.Model.Storage with
    | SessionOnly _ -> ()
    | other -> failwithf "unexpected %A" other
    Assert.True(blocked.Model.StorageNotice.IsSome)
    let chosen = select "octo/demo" blocked.Model
    let model = settle github chosen
    Assert.Equal(Some repo, model.Selection)
    let corrupt = boot (StorageOutcome.Success(Some "{garbage"))
    Assert.True(corrupt.Model.StorageNotice.IsSome)
    Assert.Equal(None, corrupt.Model.Selection)

[<Fact>]
let ``rate limiting is reported and blocks further refresh until reset`` () =
    let limited (url: string) =
        if url.EndsWith "/repos/octo/demo" then
            EffectOutcome.Success(403L, RawJson """{"message":"API rate limit exceeded"}""", Some(Map.ofList [ "x-ratelimit-remaining", "0"; "x-ratelimit-reset", string ((now.AddMinutes 30.0).ToUnixTimeSeconds()) ]))
        else github url
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let model = settle limited booted
    match model.Fetch with
    | RateLimited(Some _) -> ()
    | other -> failwithf "unexpected %A" other
    let again = Update.update Refresh model
    Assert.True(httpEffects again |> List.isEmpty)
    Assert.True(again.Model.Notice.IsSome)
    Assert.Equal(Some repo, again.Model.Selection)

[<Fact>]
let ``offline refresh keeps selection and marks data stale`` () =
    let booted = boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}"""))
    let model = settle github booted
    let offline = settle (fun _ -> EffectOutcome.Failure(HttpFailureReason.Network, None)) (Update.update Refresh model)
    match offline.Fetch with
    | Stale(Some _, _) -> ()
    | other -> failwithf "unexpected %A" other
    Assert.True((Model.currentSnapshot offline).IsSome)

[<Fact>]
let ``repository without actions or evidence renders unknown, not success`` () =
    let bare (url: string) =
        if url.Contains "/check-runs" then ok """{"total_count":0,"check_runs":[]}"""
        elif url.Contains "/actions/runs" then ok """{"total_count":0,"workflow_runs":[]}"""
        else github url
    let model = settle bare (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))
    let view = View.project model
    Assert.Equal(ViewValue.Text "Unknown", view.["headLabel"])
    Assert.Equal(ViewValue.Text "Not found", view.["coverageLabel"])
    Assert.Equal(ViewValue.Flag true, view.["noRequirements"])

[<Fact>]
let ``empty repository is handled without crashing`` () =
    let empty (url: string) =
        if url.Contains "/commits?" then status 409L """{"message":"Git Repository is empty."}""" else github url
    let model = settle empty (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))
    let view = View.project model
    Assert.Equal(ViewValue.Text "Unknown", view.["headLabel"])
    Assert.Equal(ViewValue.Flag false, view.["hasHead"])

[<Fact>]
let ``HEAD moving during refresh produces a partial snapshot`` () =
    let moving (url: string) =
        if url.Contains "/branches/" then ok """{"commit":{"sha":"cccccccccccccccccccccccccccccccccccccccc"}}""" else github url
    let model = settle moving (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))
    match model.Fetch with
    | Partial(_, reason) -> Assert.Contains("HEAD moved", reason)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``unknown semantic event is a wiring error`` () =
    Assert.ThrowsAny<exn>(fun () -> Update.ofEvent { Name = "nope"; Key = None; Value = None; Checked = None; Values = None; Submitter = None } |> ignore) |> ignore

[<Fact>]
let ``projection is total for every screen state`` () =
    let model = settle github (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))
    let keys = View.project (Model.initial now TimeZoneInfo.Utc) |> Map.keys |> Set.ofSeq
    let loaded = View.project model |> Map.keys |> Set.ofSeq
    Assert.Equal<Set<string>>(keys, loaded)

[<Fact>]
let ``rate-limited check runs with a passing workflow run never show HEAD passing`` () =
    let partial (url: string) =
        if url.Contains "/check-runs" then
            EffectOutcome.Success(403L, RawJson """{"message":"You have exceeded a secondary rate limit."}""", None)
        elif url.Contains "/actions/runs" then
            ok $"""{{"total_count":1,"workflow_runs":[{{"id":9,"name":"ci","head_sha":"{headSha}","status":"completed","conclusion":"success","run_attempt":1,"created_at":"2026-10-30T11:01:00Z","updated_at":"2026-10-30T11:05:00Z"}}]}}"""
        else github url
    let model = settle partial (boot (StorageOutcome.Success(Some """{"version":1,"selectedRepository":"octo/demo"}""")))
    match model.Fetch with
    | Partial _ -> ()
    | other -> failwithf "expected partial, got %A" other
    let view = View.project model
    Assert.Equal(ViewValue.Text "Unknown", view.["headLabel"])
    match view.["headDetail"] with
    | ViewValue.Text d -> Assert.Contains("could not be read", d)
    | other -> failwithf "unexpected %A" other
