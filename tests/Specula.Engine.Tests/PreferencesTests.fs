module Specula.Tests.PreferencesTests

open Xunit
open Specula.Engine
open Specula.Engine.Preferences
open Specula.Tests.Fixtures

[<Fact>]
let ``light is the first-run and missing-theme default`` () =
    Assert.Equal(LightTheme, Preferences.empty.Theme)
    match Preferences.decode (Some """{"version":1,"selectedRepository":"octo/demo"}""") with
    | Loaded p -> Assert.Equal(LightTheme, p.Theme)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``explicit dark and system preferences remain explicit`` () =
    let decodeTheme value =
        match Preferences.decode (Some $"""{{"version":1,"theme":"{value}"}}""") with
        | Loaded p -> p.Theme
        | other -> failwithf "unexpected %A" other
    Assert.Equal(DarkTheme, decodeTheme "dark")
    Assert.Equal(SystemTheme, decodeTheme "system")

[<Fact>]
let ``round trips version 1 preferences`` () =
    let prefs =
        { Preferences.empty with
            SelectedRepository = Some repo
            RecentRepositories = [ repo ]
            SelectedView = "activity"
            Theme = DarkTheme }
        |> Preferences.withCompetition repo { indyInitPreset with Kickoff = Some(t "2026-10-30T10:00:00Z") }
    match Preferences.decode (Some(Preferences.encode prefs)) with
    | Loaded p ->
        Assert.Equal(Some repo, p.SelectedRepository)
        Assert.Equal("activity", p.SelectedView)
        Assert.Equal(DarkTheme, p.Theme)
        Assert.Equal(Some(t "2026-10-30T10:00:00Z"), (Preferences.competitionFor repo p).Kickoff)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``migrates the unversioned shape`` () =
    match Preferences.decode (Some """{"selectedRepository":"octo/demo"}""") with
    | Migrated(p, "unversioned") -> Assert.Equal(Some repo, p.SelectedRepository)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``discards corrupt, foreign and future data safely`` () =
    let discarded s = match Preferences.decode (Some s) with Discarded _ -> true | _ -> false
    Assert.True(discarded "{not json")
    Assert.True(discarded "[1,2]")
    Assert.True(discarded """{"version":99,"selectedRepository":"octo/demo"}""")
    Assert.True(discarded """{"foo":1}""")
    Assert.Equal(NoPreferences, Preferences.decode None)

[<Fact>]
let ``invalid stored repositories are dropped, not trusted`` () =
    match Preferences.decode (Some """{"version":1,"selectedRepository":"javascript:alert(1)","recentRepositories":["octo/demo","bad repo",5]}""") with
    | Loaded p ->
        Assert.Equal(None, p.SelectedRepository)
        Assert.Equal<RepoId list>([ repo ], p.RecentRepositories)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``recent repositories are bounded and deduplicated`` () =
    let p =
        [ 1 .. 20 ]
        |> List.fold (fun acc i -> Preferences.withRecent { Owner = "o"; Name = $"r{i}" } acc) Preferences.empty
        |> Preferences.withRecent { Owner = "O"; Name = "R20" }
    Assert.Equal(Preferences.maxRecent, p.RecentRepositories.Length)
    Assert.Equal("R20", p.RecentRepositories.Head.Name)

[<Fact>]
let ``encoded preferences contain no GitHub evidence`` () =
    let text = Preferences.encode { Preferences.empty with SelectedRepository = Some repo }
    Assert.DoesNotContain("token", text)
    Assert.Contains("\"version\":1", text)
