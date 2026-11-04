module Specula.Tests.ObserverTests

open System
open System.Diagnostics
open System.IO
open Xunit
open Specula.Engine.Observer
open Specula.Engine.Observer.Types

// The historical Rehearsal 009 inputs, exactly as the replay uses them.
let private dir = Path.Combine(__SOURCE_DIRECTORY__, "..", "observer-replay", "009")
let private timelineText = File.ReadAllText(Path.Combine(dir, "timeline.json"))
let private scopeText = File.ReadAllText(Path.Combine(dir, "scope-manifest.json"))
let private timeline = Decode.timeline timelineText |> Result.defaultWith failwith
let private scope = Decode.scope scopeText |> Result.defaultWith failwith
let private at minute = Replay.atMinute timeline.Clock minute
let private findingsAt mode minute = Rules.evaluate scope (Replay.snapshotAt mode (at minute) timeline) |> snd
let private keys (fs: Finding list) = fs |> List.map (fun f -> f.Key) |> Set.ofList
let private find key (fs: Finding list) = fs |> List.find (fun f -> f.Key = key)
let private modes = [ Visibility.Local; Visibility.Remote ]
let private checkpoints = Engine.standardCheckpoints @ [ 150 ]

let private baseFacts : TreeFacts =
    { FormaStylesheetLinked = false; FormaClasses = []; CustomClassCount = 0; CssLiterals = []; CssFallbackLiterals = 0
      CssDeclaredEfProperties = []; CssUnknownEfReferences = []; FormaVersion = None; FolioDependency = false; FolioUsed = false
      LimenKernelBoot = true; LimenBoundaryFile = true; PlannedFiles = []; PlanTestFilesPresent = 0; PlanTestFilesTotal = 0
      ReferencedRequirements = []; RequirementMap = []; DeclaredVerificationScope = None; Verification = None; PraxisEvents = []
      WorkQueue = []; HandoffUpdated = true; T0Recorded = true; HiddenRequirementElements = []; InertControls = []
      ShortcutMarkers = []; CompletionArtifacts = [] }

[<Fact>]
let ``the historical inputs decode with the authoritative clock`` () =
    Assert.Equal(DateTimeOffset.Parse "2026-11-04T00:30:01Z", timeline.Clock.T0)
    Assert.Equal(DateTimeOffset.Parse "2026-11-04T07:30:01Z", timeline.Clock.Deadline)
    Assert.Equal(420, timeline.Clock.BudgetMinutes)
    Assert.Equal(161 + 38, scope.Requirements.Length)

// 1. Future commits are invisible at earlier checkpoints.
[<Fact>]
let ``future commits, runs and results are invisible at earlier checkpoints`` () =
    for mode in modes do
        for m in checkpoints do
            let t = at m
            let s = Replay.snapshotAt mode t timeline
            Assert.All(s.Commits, fun c -> Assert.True(c.CommittedAt <= t, $"{c.Sha} committed after T+{m}"))
            Assert.All(s.Runs, fun r -> Assert.True(r.Run.CreatedAt <= t))
            Assert.All(s.Runs |> List.filter (fun r -> r.Completed), fun r -> Assert.True(r.Run.CompletedAt.Value <= t))
            // An in-progress run carries no outcome or log yet.
            Assert.All(s.Runs |> List.filter (fun r -> not r.Completed), fun r -> Assert.True(r.Run.LogExcerpt.IsEmpty && r.Run.Conclusion = "in_progress"))
            // Prefix invariance: deleting everything after t changes nothing.
            let full = Rules.evaluate scope s |> snd
            let cut = Rules.evaluate scope (Replay.snapshotAt mode t (Replay.truncate t timeline)) |> snd
            Assert.Equal<Finding list>(full, cut)

[<Fact>]
let ``a commit is invisible before it is pushed in remote mode`` () =
    let edde = timeline.Commits |> List.find (fun c -> c.Sha.StartsWith "edde38c")
    // Committed at T+21 but its first CI evidence is at T+43.
    Assert.True(Replay.snapshotAt Visibility.Local (at 30) timeline |> fun s -> s.Commits |> List.exists (fun c -> c.Sha = edde.Sha))
    Assert.False(Replay.snapshotAt Visibility.Remote (at 30) timeline |> fun s -> s.Commits |> List.exists (fun c -> c.Sha = edde.Sha))

// 2. The final report is unavailable during blind replay.
[<Fact>]
let ``the final report's content is never in the replay inputs or outputs`` () =
    let truth = File.ReadAllText(Path.Combine(dir, "reveal", "ground-truth.json"))
    let phrases =
        (Specula.Engine.Json.tryParse truth).Value
        |> Specula.Engine.Json.array "sealedPhrases"
        |> List.choose Specula.Engine.Json.asString
    Assert.NotEmpty phrases
    for p in phrases do
        Assert.DoesNotContain(p, timelineText)
        Assert.DoesNotContain(p, scopeText)
    // The only trace of it is a path and a digest.
    let sealedEntry = timeline.Sealed |> List.exactlyOne
    Assert.Equal("evidence/FINAL-REPORT.md", sealedEntry.Path)
    for mode in modes do
        for m in checkpoints do
            let text = sprintf "%A" (findingsAt mode m)
            for p in phrases do Assert.DoesNotContain(p, text)

[<Fact>]
let ``a sealed artifact's content field is dropped by the decoder`` () =
    let tampered = timelineText.Replace("\"reason\": \"The builder's final report.", "\"content\": \"SECRET-REPORT-BODY\", \"reason\": \"The builder's final report.")
    Assert.Contains("SECRET-REPORT-BODY", tampered)
    let decoded = Decode.timeline tampered |> Result.defaultWith failwith
    Assert.DoesNotContain("SECRET-REPORT-BODY", sprintf "%A" decoded)

// 3. A narrow completed catalog does not establish full application completion.
[<Fact>]
let ``a verified narrow catalog does not establish completion`` () =
    for mode in modes do
        let s = Replay.snapshotAt mode (at 80) timeline
        let ctx, fs = Rules.evaluate scope s
        let p = Rules.progress ctx
        Assert.Equal(32, p.VerifiedCatalog)
        Assert.Equal(0, p.VerifiedFrozenPlan)
        let f = find "OBS-001/narrow-catalog-complete" fs
        Assert.Equal(Severity.Critical, f.Severity)
        Assert.True f.Intervention
        Assert.Equal(161, f.Requirements.Length)

[<Fact>]
let ``the catalog-only verification scope is flagged as soon as it is declared`` () =
    let f = find "OBS-001/declared-scope" (findingsAt Visibility.Local 30)
    Assert.Equal(Severity.Warning, f.Severity)
    Assert.DoesNotContain("OBS-001/declared-scope", keys (findingsAt Visibility.Local 25))

// 4. Importing Forma alone does not establish design-contract compliance.
[<Fact>]
let ``importing Forma without its layout patterns violates the contract`` () =
    let importedOnly = { baseFacts with FormaStylesheetLinked = true; FormaClasses = [ "ef-button"; "ef-field"; "ef-form" ] }
    let violated = Rules.violatedContracts scope importedOnly |> List.map (fun (c, _) -> c.Id) |> Set.ofList
    Assert.True(Set.isSubset (set [ "FL-SHELL"; "FL-07"; "FL-08"; "FL-09" ]) violated)
    let allPatterns = scope.DesignContracts |> List.collect (fun c -> c.RequiredClasses)
    let compliant = { importedOnly with FormaClasses = allPatterns; CssFallbackLiterals = 12 }
    // Custom CSS that only uses tokens (with var() fallbacks) is not a violation.
    Assert.Empty(Rules.violatedContracts scope compliant)
    let literal = { compliant with CssLiterals = [ { Selector = ".x"; Value = "color: #8a5a00" } ] }
    Assert.Equal<string list>([ "FL-TOKENS" ], Rules.violatedContracts scope literal |> List.map (fun (c, _) -> c.Id))

[<Fact>]
let ``the historical CSS regression is detected at the first markup commit`` () =
    // edde38c, the first markup commit, was committed at T+21.3.
    let f = find "OBS-003/design-contract" (findingsAt Visibility.Local 22)
    Assert.Contains("UI-020", f.Requirements)
    Assert.Contains(f.Evidence, fun e -> e.Contains "indy-init-rehearsal-008")
    Assert.DoesNotContain("OBS-003/design-contract", keys (findingsAt Visibility.Local 20))

// 5. Broken release-gate infrastructure does not imply broken functionality.
[<Fact>]
let ``a gate whose suite runner produced no results is an infrastructure blocker`` () =
    let gate = timeline.Runs |> List.find (fun r -> r.Id = 41752595190L)
    let cls = Rules.classify gate
    Assert.Equal(Rules.FailureClass.SuiteInfrastructure, cls)
    Assert.False(Rules.impliesApplicationDefect cls)
    let fs = findingsAt Visibility.Local 50
    let f = find "OBS-005/Verification gate" fs
    Assert.Equal(Severity.Warning, f.Severity)
    Assert.Contains("not proof that the application is defective", f.Why)
    Assert.Contains(f.Evidence, fun e -> e.StartsWith "Build and test passed at the same commit")
    // The gate failure alone raises no critical finding.
    Assert.DoesNotContain(fs, fun f -> f.Severity = Severity.Critical)

// 6. Stopping with unfinished mandatory scope and time remaining is critical.
[<Fact>]
let ``completion and stopping with mandatory scope unfinished are critical`` () =
    let declared = find "OBS-004/completion-declared" (findingsAt Visibility.Local 115)
    Assert.Equal(Severity.Critical, declared.Severity)
    Assert.True declared.Intervention
    let stopped = find "OBS-004/stopped" (findingsAt Visibility.Local 150)
    Assert.Equal(Severity.Critical, stopped.Severity)
    Assert.DoesNotContain("OBS-004/stopped", keys (findingsAt Visibility.Local 115))
    Assert.DoesNotContain("OBS-004/completion-declared", keys (findingsAt Visibility.Local 100))

// 7. Duplicate alerts are suppressed.
[<Fact>]
let ``repeated findings raise one alert until severity or evidence changes`` () =
    let f = find "OBS-003/design-contract" (findingsAt Visibility.Local 30)
    let a1, s1 = Alerts.step "T" timeline.Clock (at 30) [ f ] Alerts.empty
    let a2, s2 = Alerts.step "T" timeline.Clock (at 40) [ f ] s1
    Assert.Equal(1, a1.Length)
    Assert.Empty a2
    let a3, s3 = Alerts.step "T" timeline.Clock (at 50) [ { f with Severity = Severity.Critical } ] s2
    Assert.Equal(AlertChange.Escalated, (List.exactlyOne a3).Change)
    let a4, s4 = Alerts.step "T" timeline.Clock (at 60) [ { f with Severity = Severity.Critical; Fingerprint = "other" } ] s3
    Assert.Equal(AlertChange.EvidenceChanged, (List.exactlyOne a4).Change)
    let a5, _ = Alerts.step "T" timeline.Clock (at 70) [] s4
    Assert.Equal(AlertChange.Resolved, (List.exactlyOne a5).Change)
    // Across the whole historical replay the design-contract alert is sent once.
    let replay = Engine.replay scope timeline Visibility.Local (Replay.evaluationPoints Visibility.Local checkpoints 150 timeline)
    let sent = replay |> List.collect (fun c -> c.Alerts) |> List.filter (fun a -> a.Finding.Key = "OBS-003/design-contract")
    Assert.Equal(1, sent.Length)

// 8. Recovery handoffs preserve the original deadline.
[<Fact>]
let ``the handoff keeps the original T0 and deadline and is consistent`` () =
    for mode in modes do
        let s = Replay.snapshotAt mode (at 115) timeline
        let fs = Rules.evaluate scope s |> snd
        let h = Handoff.generate scope s fs
        Assert.Equal(timeline.Clock.T0, h.T0)
        Assert.Equal(timeline.Clock.Deadline, h.Deadline)
        Assert.Equal(305, h.RemainingMinutes)
        Assert.Empty(Handoff.validate scope s h)
        Assert.Contains(h.ClockInstructions, fun i -> i.Contains "Do not record a new T0")
        // A handoff that restarts the clock fails validation.
        let restarted = { h with T0 = s.At; Deadline = s.At.AddMinutes 420.0 }
        Assert.Contains(Handoff.validate scope s restarted, fun v -> v.Contains "deadline")

// 9. The observer cannot modify the monitored repository.
[<Fact>]
let ``write commands and write HTTP methods are refused`` () =
    for cmd in [ [ "push"; "origin"; "main" ]; [ "commit"; "-m"; "x" ]; [ "reset"; "--hard" ]; [ "merge"; "x" ]; [ "checkout"; "x" ]
                 [ "config"; "user.name"; "x" ]; [ "tag"; "v1" ]; [ "update-ref"; "HEAD"; "x" ]; [ "fetch" ]; [ "gc" ]; [ "status" ]
                 [ "log"; "--output=/tmp/x" ]; [ "show"; "-c"; "x" ]; [] ] do
        Assert.True(Result.isError (ReadOnly.admit cmd), sprintf "%A was admitted" cmd)
    for cmd in [ [ "log"; "--all" ]; [ "show"; "HEAD:README.md" ]; [ "ls-tree"; "-r"; "HEAD" ]; [ "cat-file"; "-p"; "abc" ] ] do
        Assert.True(Result.isOk (ReadOnly.admit cmd))
    for m in [ "POST"; "PUT"; "PATCH"; "DELETE" ] do
        Assert.True(Result.isError (ReadOnly.admitHttpMethod m))
    Assert.True(Result.isOk (ReadOnly.admitHttpMethod "GET"))

let private sh (cwd: string) (file: string) (args: string list) =
    let info = ProcessStartInfo(file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd)
    args |> List.iter info.ArgumentList.Add
    use p = Process.Start info
    let out = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    out

[<Fact>]
let ``collecting from a repository leaves its refs, index and files unchanged`` () =
    let repo = Path.Combine(Path.GetTempPath(), "observer-ro-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory repo |> ignore
    let g args = sh repo "git" args |> ignore
    g [ "init"; "-q"; "-b"; "main" ]
    g [ "config"; "user.email"; "t@example.com" ]
    g [ "config"; "user.name"; "t" ]
    File.WriteAllText(Path.Combine(repo, "a.txt"), "one")
    g [ "add"; "a.txt" ]
    g [ "commit"; "-q"; "-m"; "first" ]
    // Refs, HEAD, the working file and the index bytes. (git status is not
    // used here: it refreshes the index itself.)
    let state () =
        let hash (p: string) = Convert.ToHexString(Security.Cryptography.SHA256.HashData(File.ReadAllBytes p))
        sh repo "git" [ "for-each-ref" ] + sh repo "git" [ "rev-parse"; "HEAD" ]
        + hash (Path.Combine(repo, "a.txt")) + hash (Path.Combine(repo, ".git", "index"))
        + string (Directory.GetFiles(Path.Combine(repo, ".git"), "*", SearchOption.AllDirectories).Length)
    let before = state ()
    let commits = Specula.Observer.Cli.Collect.commits repo
    let tree = Specula.Observer.Cli.Collect.tree repo commits.Head.Sha
    let _ = Specula.Observer.Cli.Collect.changedFiles repo commits.Head
    Assert.True(tree.ContainsKey "a.txt")
    // A write routed through the observer's git runner never runs.
    Assert.True(Result.isError (Specula.Observer.Cli.Common.git repo [ "commit"; "--allow-empty"; "-m"; "observer" ]))
    Assert.True(Result.isError (Specula.Observer.Cli.Common.git repo [ "reset"; "--hard"; "HEAD" ]))
    Assert.Equal(before, state ())

// 10. Missing evidence produces an uncertainty, not a fabricated conclusion.
[<Fact>]
let ``a failure without a readable cause is an uncertainty`` () =
    // Historical: Build and test at 1f2206b failed; its collected log tail has no cause.
    let f = find "OBS-005/Build and test" (findingsAt Visibility.Local 45)
    Assert.Equal(FindingKind.Uncertainty, f.Kind)
    Assert.Equal(Severity.Informational, f.Severity)
    Assert.False f.Intervention
    // A run whose log was never collected is never classified as a defect.
    let gate = timeline.Runs |> List.find (fun r -> r.Id = 41752595190L)
    Assert.Equal(Rules.FailureClass.Unknown, Rules.classify { gate with LogCollected = false })

[<Fact>]
let ``commits without CI evidence yield an uncertainty, not a failure`` () =
    let fs = findingsAt Visibility.Local 42
    let f = find "OBS-002/no-ci" fs
    Assert.Equal(FindingKind.Uncertainty, f.Kind)
    Assert.DoesNotContain(fs, fun x -> x.Rule = "OBS-005" && x.Kind = FindingKind.Observation)

[<Fact>]
let ``no functional-shortcut alert is raised without evidence`` () =
    for m in checkpoints do
        Assert.DoesNotContain(findingsAt Visibility.Local m, fun f -> f.Rule = "OBS-007")

[<Fact>]
let ``Praxis registration after implementation began is detected`` () =
    let before = find "OBS-006/sequencing" (findingsAt Visibility.Local 30)
    Assert.Contains("no Praxis work item", before.Title)
    let after = find "OBS-006/sequencing" (findingsAt Visibility.Local 45)
    Assert.Contains("21 minutes after", after.Title)

[<Fact>]
let ``a fresh replay reproduces every committed checkpoint record byte for byte`` () =
    let index = (Specula.Engine.Json.tryParse (File.ReadAllText(Path.Combine(dir, "results", "checkpoints", "INDEX.json")))).Value
    let expected =
        Specula.Engine.Json.array "records" index
        |> List.map (fun r -> Specula.Engine.Json.str "file" r |> Option.get, Specula.Engine.Json.str "sha256" r |> Option.get)
    let digest = Specula.Observer.Cli.Common.sha256Text (Specula.Observer.Cli.Common.sha256Text scopeText + Specula.Observer.Cli.Common.sha256Text timelineText)
    let actual =
        modes
        |> List.collect (fun mode ->
            Engine.replay scope timeline mode (Replay.evaluationPoints mode checkpoints 150 timeline)
            |> List.mapi (fun i c ->
                let name = sprintf "%s/%03d-T%+04d.json" (visibilityCode mode) i c.Minute
                name, Specula.Observer.Cli.Common.sha256Text (Specula.Observer.Cli.Common.render (Specula.Observer.Cli.Output.checkpoint digest c))))
    Assert.Equal<(string * string) list>(expected, actual)
