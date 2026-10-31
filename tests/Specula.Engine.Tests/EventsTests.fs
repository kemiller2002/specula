module Specula.Tests.EventsTests

open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

let commit sha date : Commit =
    { Sha = sha; Message = "m " + sha; AuthorName = None; AuthorLogin = Some "a"; Date = Some(t date); HtmlUrl = $"https://github.com/octo/demo/commit/{sha}" }

[<Fact>]
let ``deduplicates by stable id and orders newest first`` () =
    let events =
        [ commit "a" "2026-10-30T10:00:00Z"; commit "b" "2026-10-30T11:00:00Z"; commit "a" "2026-10-30T10:00:00Z" ]
        |> List.choose Events.ofCommit
        |> Events.normalize
    Assert.Equal<string list>([ "commit:b"; "commit:a" ], events |> List.map (fun e -> e.StableId))

[<Fact>]
let ``repeated merges of the same page do not duplicate records`` () =
    let page = [ commit "a" "2026-10-30T10:00:00Z"; commit "b" "2026-10-30T11:00:00Z" ]
    let merged = [ 1 .. 10 ] |> List.fold (fun acc _ -> Events.merge (fun (c: Commit) -> c.Sha) acc page) []
    Assert.Equal(2, List.length merged)

[<Fact>]
let ``a merged pull request yields opened and merged events bound to their SHAs`` () =
    let pr : PullRequest =
        { Number = 4L; Title = "Fix"; State = PullMerged; Draft = false; Author = Some "bob"
          CreatedAt = Some(t "2026-10-30T09:00:00Z"); UpdatedAt = None; ClosedAt = None; MergedAt = Some(t "2026-10-30T10:00:00Z")
          HeadSha = Some "head1"; MergeSha = Some "merge1"; HtmlUrl = "https://github.com/octo/demo/pull/4" }
    let events = Events.ofPull pr
    Assert.Equal(2, events.Length)
    Assert.Equal(Some "merge1", (events |> List.find (fun e -> e.StableId.EndsWith "merged")).Sha)

[<Fact>]
let ``records without timestamps are not placed on the timeline`` () =
    Assert.True((Events.ofCommit { commit "x" "2026-10-30T10:00:00Z" with Date = None }).IsNone)

[<Fact>]
let ``filters select the right kinds`` () =
    let e = (Events.ofCommit (commit "a" "2026-10-30T10:00:00Z")).Value
    Assert.True(Events.matches Events.OnlyCommits e)
    Assert.False(Events.matches Events.OnlyChecks e)
    Assert.True(Events.matches Events.AllEvents e)
