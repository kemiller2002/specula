module Specula.Tests.Fixtures

open System
open Specula.Engine

let repo = { Owner = "octo"; Name = "demo" }
let t (s: string) = DateTimeOffset.Parse(s).ToUniversalTime()
let json (s: string) = (Json.tryParse s).Value

let run id name sha outcome created updated attempt : WorkflowRun =
    { Id = id; Name = name; HeadSha = sha; HeadBranch = Some "main"; Event = Some "push"; Outcome = outcome
      RunAttempt = attempt; CreatedAt = Some(t created); UpdatedAt = Some(t updated); HtmlUrl = $"https://github.com/octo/demo/actions/runs/{id}" }

let check id name sha outcome : CheckRun =
    { Id = id; Name = name; HeadSha = sha; Outcome = outcome; App = Some "GitHub Actions"
      StartedAt = Some(t "2026-10-30T10:00:00Z"); CompletedAt = Some(t "2026-10-30T10:05:00Z"); HtmlUrl = $"https://github.com/octo/demo/runs/{id}" }

let commitJson sha message date =
    $"""{{"sha":"{sha}","html_url":"https://github.com/octo/demo/commit/{sha}","commit":{{"message":"{message}","author":{{"name":"A","date":"{date}"}},"committer":{{"date":"{date}"}}}},"author":{{"login":"alice"}}}}"""
