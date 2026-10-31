module Specula.Tests.GitHubTests

open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

[<Fact>]
let ``maps status and conclusion to the closed outcome set`` () =
    Assert.Equal(Passed, GitHub.outcomeOf (Some "completed") (Some "success"))
    Assert.Equal(Failed, GitHub.outcomeOf (Some "completed") (Some "failure"))
    Assert.Equal(Failed, GitHub.outcomeOf (Some "completed") (Some "timed_out"))
    Assert.Equal(Cancelled, GitHub.outcomeOf (Some "completed") (Some "cancelled"))
    Assert.Equal(Skipped, GitHub.outcomeOf (Some "completed") (Some "skipped"))
    Assert.Equal(Queued, GitHub.outcomeOf (Some "queued") None)
    Assert.Equal(Running, GitHub.outcomeOf (Some "in_progress") None)
    Assert.Equal(UnknownOutcome, GitHub.outcomeOf (Some "completed") (Some "something-new"))
    Assert.Equal(UnknownOutcome, GitHub.outcomeOf None None)

[<Fact>]
let ``parses commits and tolerates malformed records`` () =
    let body = "[" + commitJson "abc123" "first line\\nbody" "2026-10-30T10:00:00Z" + ", {\"nope\":1}, 42]"
    let commits = GitHub.parseCommits repo (json body)
    Assert.Single(commits) |> ignore
    Assert.Equal("first line", GitHub.firstLine commits.Head.Message)
    Assert.Equal(Some "alice", commits.Head.AuthorLogin)

[<Fact>]
let ``rejects non-GitHub html_url values in favour of a constructed link`` () =
    let body = """[{"sha":"abc","html_url":"javascript:alert(1)","commit":{"message":"m","committer":{"date":"2026-10-30T10:00:00Z"}}}]"""
    let c = (GitHub.parseCommits repo (json body)).Head
    Assert.Equal("https://github.com/octo/demo/commit/abc", c.HtmlUrl)

[<Fact>]
let ``pull requests distinguish open, merged and closed`` () =
    let body = """[{"number":1,"title":"a","state":"open","html_url":"https://github.com/octo/demo/pull/1"},
                   {"number":2,"title":"b","state":"closed","merged_at":"2026-10-30T10:00:00Z"},
                   {"number":3,"title":"c","state":"closed","closed_at":"2026-10-30T10:00:00Z"}]"""
    let states = GitHub.parsePulls repo (json body) |> List.map (fun p -> p.State)
    Assert.Equal<PullState list>([ PullOpen; PullMerged; PullClosed ], states)

[<Fact>]
let ``classifies rate limiting, not found and empty repositories`` () =
    let now = t "2026-10-30T10:00:00Z"
    let headers = Map.ofList [ "X-RateLimit-Remaining", "0"; "X-RateLimit-Limit", "60"; "X-RateLimit-Reset", "1793360000" ]
    match GitHub.classify now 403L None headers with
    | GitHub.Forbidden(true, Some _, _) -> ()
    | other -> failwithf "unexpected %A" other
    match GitHub.classify now 429L None (Map.ofList [ "retry-after", "30" ]) with
    | GitHub.Forbidden(true, Some at, _) -> Assert.Equal(now.AddSeconds 30.0, at)
    | other -> failwithf "unexpected %A" other
    match GitHub.classify now 403L (Some(json """{"message":"Resource not accessible"}""")) Map.empty with
    | GitHub.Forbidden(false, _, _) -> ()
    | other -> failwithf "unexpected %A" other
    Assert.Equal(GitHub.NotFound, GitHub.classify now 404L None Map.empty)
    Assert.Equal(GitHub.NotModified, GitHub.classify now 304L None Map.empty)
    match GitHub.classify now 409L (Some(json """{"message":"Git Repository is empty."}""")) Map.empty with
    | GitHub.Conflict m -> Assert.Contains("empty", m)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``reads Link header pagination and rate limit headers`` () =
    Assert.True(GitHub.hasNextPage (Some "<https://api.github.com/x?page=2>; rel=\"next\", <https://api.github.com/x?page=5>; rel=\"last\""))
    Assert.False(GitHub.hasNextPage (Some "<https://api.github.com/x?page=1>; rel=\"prev\""))
    Assert.False(GitHub.hasNextPage None)
    let rl = (GitHub.rateLimitOf (Map.ofList [ "x-ratelimit-limit", "60"; "x-ratelimit-remaining", "12" ])).Value
    Assert.Equal(Some 12L, rl.Remaining)
