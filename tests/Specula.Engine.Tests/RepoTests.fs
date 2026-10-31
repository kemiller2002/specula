module Specula.Tests.RepoTests

open Xunit
open Specula.Engine

let ok input owner name =
    match Repo.parse input with
    | Ok r -> Assert.Equal(owner, r.Owner); Assert.Equal(name, r.Name)
    | Error e -> failwithf "expected ok for %s, got %s" input (Repo.describe e)

let err input =
    match Repo.parse input with
    | Ok r -> failwithf "expected error for %s, got %s" input (RepoId.text r)
    | Error _ -> ()

[<Fact>]
let ``accepts owner/repo, URLs and normalizes`` () =
    ok "kemiller2002/specula" "kemiller2002" "specula"
    ok "  kemiller2002/specula  " "kemiller2002" "specula"
    ok "https://github.com/kemiller2002/specula" "kemiller2002" "specula"
    ok "https://github.com/kemiller2002/specula/" "kemiller2002" "specula"
    ok "https://github.com/kemiller2002/specula.git" "kemiller2002" "specula"
    ok "https://www.github.com/kemiller2002/specula/tree/main/src" "kemiller2002" "specula"
    ok "github.com/kemiller2002/specula" "kemiller2002" "specula"
    ok "a/b.c_d-e" "a" "b.c_d-e"

[<Fact>]
let ``rejects malformed, hostile and foreign inputs`` () =
    err ""
    err "   "
    err "specula"
    err "javascript:alert(1)"
    err "http://github.com/a/b"
    err "https://gitlab.com/a/b"
    err "https://github.com.evil.example/a/b"
    err "gitlab.com/a/b"
    err "-bad/repo"
    err "bad-/repo"
    err "a/.."
    err "a/b c"
    err "a/b/c"
    err (String.replicate 40 "a" + "/x")

[<Fact>]
let ``only https github.com links are treated as source links`` () =
    Assert.True(Repo.isGitHubUrl "https://github.com/a/b/commit/1")
    Assert.False(Repo.isGitHubUrl "http://github.com/a/b")
    Assert.False(Repo.isGitHubUrl "https://github.com.evil.example/x")
    Assert.False(Repo.isGitHubUrl "javascript:alert(1)")
    Assert.False(Repo.isGitHubUrl "https://user@github.com/a")

[<Fact>]
let ``repository identity is case-insensitive`` () =
    Assert.True(RepoId.same { Owner = "A"; Name = "B" } { Owner = "a"; Name = "b" })
