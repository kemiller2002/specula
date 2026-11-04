namespace Specula.Observer.Cli

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Specula.Engine
open Specula.Observer.Cli.Common

/// Read-only collection of the historical timeline. Every repository read
/// goes through Common.git (ReadOnly.admit). Facts are recomputed only for
/// files a commit changed; unchanged blobs are served from the cache.
module Collect =

    let sealedPaths = [ "evidence/FINAL-REPORT.md" ]

    let unobservable =
        [ "The builder's session transcript and tool calls are not published; work between commits is unobservable."
          "Push times were not retrieved; remote visibility is approximated by the first CI run on a commit or a descendant (an upper bound)."
          "CI artifacts (suite run.log files) were not downloaded; only job-log tails of failed runs were collected, and some tails stop before the cause line." ]

    type Blobs = Map<string, string>

    let private str (name: string) (el: JsonElement) = Json.str name el |> Option.defaultValue ""

    // ---- text extraction (pure) ----

    let tagRx = Regex("<(input|textarea|select|button|a|section|div|p|span|template|li|form|label|fieldset|ul|nav|header|main)\\b[^>]*>", RegexOptions.IgnoreCase)
    let classAttrRx = Regex("class=\"([^\"]*)\"")

    let classesIn (text: string) =
        classAttrRx.Matches text
        |> Seq.collect (fun m -> m.Groups.[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        |> Seq.toList

    let efTokensIn (text: string) =
        Regex.Matches(text, "\\bef-[a-z0-9_-]+") |> Seq.map (fun m -> m.Value) |> Seq.toList

    let hiddenRequirementElements (html: string) =
        tagRx.Matches html
        |> Seq.map (fun m -> m.Value)
        |> Seq.filter (fun t -> t.Contains "data-requirement-id")
        |> Seq.filter (fun t ->
            Regex.IsMatch(t, "\\shidden(\\s|>|=)") || t.Contains "aria-hidden=\"true\"" || Regex.IsMatch(t, "display\\s*:\\s*none") || Regex.IsMatch(t, "class=\"[^\"]*\\b(hidden|visually-hidden|ef-visually-hidden|sr-only)\\b"))
        |> Seq.map (fun t -> Regex.Match(t, "data-requirement-id=\"([^\"]+)\"").Groups.[1].Value + ": " + t)
        |> Seq.toList

    let inertControls (html: string) =
        tagRx.Matches html
        |> Seq.map (fun m -> m.Value)
        |> Seq.filter (fun t -> Regex.IsMatch(t, "^<(input|textarea|select)\\b", RegexOptions.IgnoreCase))
        |> Seq.filter (fun t -> not (Regex.IsMatch(t, "type=\"(hidden|submit|button|reset)\"")))
        |> Seq.filter (fun t -> not (t.Contains "data-event") && not (t.Contains "data-bind"))
        |> Seq.toList

    /// CSS declarations as (selector, property, value).
    let declarations (css: string) =
        let noComments = Regex.Replace(css, "/\\*.*?\\*/", "", RegexOptions.Singleline)
        Regex.Matches(noComments, "([^{}]+)\\{([^{}]*)\\}")
        |> Seq.collect (fun m ->
            let selector = m.Groups.[1].Value.Trim()
            m.Groups.[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            |> Seq.choose (fun d ->
                match d.IndexOf ':' with
                | i when i > 0 -> Some(selector, d.Substring(0, i).Trim(), d.Substring(i + 1).Trim())
                | _ -> None))
        |> Seq.toList

    let colourRx = Regex("#[0-9a-fA-F]{3,8}\\b|\\b(rgb|rgba|hsl|hsla)\\(")

    /// Remove var(...) calls, including their fallbacks, so only literal
    /// colours written outside a token reference remain.
    let rec stripVars (value: string) =
        let i = value.IndexOf "var("
        if i < 0 then value
        else
            let rec close depth j =
                if j >= value.Length then value.Length
                elif value.[j] = '(' then close (depth + 1) (j + 1)
                elif value.[j] = ')' then (if depth = 1 then j + 1 else close (depth - 1) (j + 1))
                else close depth (j + 1)
            let e = close 0 (i + 3)
            stripVars (value.Substring(0, i) + " " + value.Substring(e))

    let cssFacts (inventory: Set<string>) (css: string) =
        let decls = declarations css
        let literals =
            decls
            |> List.filter (fun (_, p, v) -> not (p.StartsWith "--") && colourRx.IsMatch(stripVars v))
            |> List.map (fun (s, p, v) -> s, $"{p}: {v}")
        let fallbackLiterals =
            decls |> List.filter (fun (_, _, v) -> v.Contains "var(" && colourRx.IsMatch v && not (colourRx.IsMatch(stripVars v))) |> List.length
        let declared = decls |> List.filter (fun (_, p, _) -> p.StartsWith "--ef-") |> List.map (fun (_, p, _) -> p) |> List.distinct
        let referenced = Regex.Matches(css, "var\\(\\s*(--ef-[a-z0-9-]+)") |> Seq.map (fun m -> m.Groups.[1].Value) |> Seq.distinct |> Seq.toList
        literals, fallbackLiterals, declared, referenced |> List.filter (fun r -> not (inventory.Contains r))

    let shortcutRx = Regex("\\b(TODO|FIXME|not implemented|NotImplemented|mock(ed)?|fake|stubbed)\\b", RegexOptions.IgnoreCase)

    let shortcutMarkers (path: string) (text: string) =
        text.Split('\n')
        |> Array.mapi (fun i l -> i + 1, l)
        |> Array.filter (fun (_, l) -> shortcutRx.IsMatch l)
        |> Array.map (fun (n, l) -> $"{path}:{n}: {l.Trim()}")
        |> Array.toList

    let isStub (text: string) =
        text.Split('\n') |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l)) |> Array.length <= 3

    // ---- git reads ----

    type Tree = Map<string, string> // path -> blob id

    let tree (repo: string) (sha: string) : Tree =
        lines (gitOr repo [ "ls-tree"; "-r"; sha ] "")
        |> List.choose (fun l ->
            match l.Split('\t') with
            | [| meta; path |] ->
                match meta.Split(' ') with
                | [| _; "blob"; blob |] -> Some(path, blob)
                | _ -> None
            | _ -> None)
        |> Map.ofList

    let blob (repo: string) (cache: Blobs) (id: string) : string * Blobs =
        match cache |> Map.tryFind id with
        | Some text -> text, cache
        | None ->
            let text = gitOr repo [ "cat-file"; "-p"; id ] ""
            text, cache |> Map.add id text

    let read repo (cache: Blobs) (t: Tree) (path: string) : string option * Blobs =
        match t |> Map.tryFind path with
        | None -> None, cache
        | Some id ->
            let text, c = blob repo cache id
            Some text, c

    // ---- collection ----

    type FoldState =
        { Cache: Blobs
          Refs: Map<string, string list>
          PrevMapped: Set<string>
          PrevImplemented: int
          PrevClasses: string list
          PrevLiterals: int
          Nodes: JsonNode list }

    type CommitInfo =
        { Sha: string
          Parents: string list
          CommittedAt: DateTimeOffset
          Subject: string }

    let commits (repo: string) =
        lines (gitOr repo [ "log"; "--all"; "--reverse"; "--date-order"; "--format=%H%x09%P%x09%cI%x09%s" ] "")
        |> List.map (fun l ->
            let parts = l.Split('\t')
            { Sha = parts.[0]
              Parents = parts.[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
              CommittedAt = DateTimeOffset.Parse(parts.[2]).ToUniversalTime()
              Subject = if parts.Length > 3 then parts.[3] else "" })

    let revList repo (refs: string list) = lines (gitOr repo ("rev-list" :: refs) "") |> Set.ofList

    let changedFiles repo (c: CommitInfo) =
        let range =
            match c.Parents with
            | [] -> [ "--root"; c.Sha ]
            | p :: _ -> [ p; c.Sha ]
        let numstat = lines (gitOr repo ([ "diff-tree"; "-r"; "--no-commit-id"; "--numstat"; "--no-renames" ] @ range) "")
        let status = lines (gitOr repo ([ "diff-tree"; "-r"; "--no-commit-id"; "--name-status"; "--no-renames" ] @ range) "")
        let statusOf = status |> List.choose (fun l -> match l.Split('\t') with | [| s; p |] -> Some(p, s) | _ -> None) |> Map.ofList
        numstat
        |> List.choose (fun l ->
            match l.Split('\t') with
            | [| a; d; p |] ->
                let n (s: string) = match Int32.TryParse s with | true, v -> v | _ -> 0
                Some(p, statusOf |> Map.tryFind p |> Option.defaultValue "M", n a, n d)
            | _ -> None)

    let run (argv: string list) =
        let started = Diagnostics.Stopwatch.StartNew()
        let repo = requiredArg "--repo" argv
        let scopePath = requiredArg "--scope" argv
        let indyInit = requiredArg "--indy-init" argv
        let runsPath = requiredArg "--runs" argv
        let logsPath = requiredArg "--logs" argv
        let out = requiredArg "--out" argv
        let scope = readJson scopePath
        let inventory = Json.path [ "formaInventory"; "customProperties" ] scope |> Option.map Json.asArray |> Option.defaultValue [] |> List.choose Json.asString |> Set.ofList
        let formaClasses =
            readJson (Path.Combine(indyInit, "requirements-compiled/v4.1.2/analysis/forma-0.12.0-inventory.json"))
            |> Json.array "classes" |> List.choose Json.asString |> Set.ofList
        let reqs = Json.array "requirements" scope
        let knownIds = reqs |> List.map (str "id")
        let titleOf = reqs |> List.map (fun r -> str "id" r, str "title" r) |> Map.ofList
        let idRx =
            let alternatives = knownIds |> List.sortByDescending (fun s -> s.Length) |> List.map Regex.Escape
            Regex("(?<![A-Za-z0-9.§-])(" + String.Join("|", alternatives) + ")(?![A-Za-z0-9-]|\\.[0-9])")
        let ownership = readJson (Path.Combine(indyInit, "requirements-compiled/v4.1.2/agent-file-ownership.json"))
        let planned =
            Json.array "files" ownership
            |> List.map (fun f -> str "path" f, str "owner" f)
            |> List.filter (fun (p, _) -> Regex.IsMatch(p, "\\.(fs|cs|js|mjs|ts|css|html)$"))
        let planTests = planned |> List.filter (fun (p, _) -> p.StartsWith "tests/")
        let all = commits repo
        let heads = lines (gitOr repo [ "for-each-ref"; "--format=%(refname:short)"; "refs/heads" ] "")
        let dataBranch = heads |> List.tryFind (fun h -> h = "causa-data")
        let builderHeads = heads |> List.filter (fun h -> Some h <> dataBranch)
        let builderSet = revList repo builderHeads
        let mainSet = revList repo [ "main" ]
        let dataOnly = all |> List.filter (fun c -> not (builderSet.Contains c.Sha)) |> List.map (fun c -> c.Sha) |> Set.ofList
        // Authoritative clock from the T0 record, read at the first commit that has it.
        let t0Commit, t0Text =
            all
            |> List.filter (fun c -> builderSet.Contains c.Sha)
            |> List.tryPick (fun c ->
                match git repo [ "show"; $"{c.Sha}:evidence/t0/T0.md" ] with
                | Ok text -> Some(c.Sha, text)
                | Error _ -> None)
            |> Option.defaultWith (fun () -> failwith "no evidence/t0/T0.md: the authoritative T0 cannot be established")
        let t0 = Regex.Match(t0Text, "Timed start \\(T0\\):\\s*(\\S+)").Groups.[1].Value |> DateTimeOffset.Parse
        let dl = Regex.Match(t0Text, "Deadline \\(T0 \\+ (\\d+) min\\):\\s*(\\S+)")
        let budget = int dl.Groups.[1].Value
        let deadline = DateTimeOffset.Parse dl.Groups.[2].Value
        if deadline <> t0.AddMinutes(float budget) then failwith "T0 record is inconsistent: deadline is not T0 plus the budget"
        let bootstrapSha =
            all |> List.filter (fun c -> builderSet.Contains c.Sha && c.CommittedAt >= t0) |> List.head |> fun c -> c.Sha
        // CI runs and their visibility.
        let runsRaw = Json.array "workflow_runs" (readJson runsPath)
        let logs = Json.array "logs" (readJson logsPath) |> List.map (fun l -> Json.int64 "runId" l |> Option.defaultValue 0L, l) |> Map.ofList
        let ancestorsOf =
            runsRaw
            |> List.map (str "head_sha")
            |> List.distinct
            |> List.map (fun h -> h, revList repo [ h ])
            |> Map.ofList
        let remoteVisible (sha: string) =
            runsRaw
            |> List.filter (fun r -> ancestorsOf |> Map.tryFind (str "head_sha" r) |> Option.exists (fun s -> s.Contains sha))
            |> List.choose (Json.time "created_at")
            |> List.sort
            |> List.tryHead
        let bootstrapTree = tree repo bootstrapSha
        let builderCommits = all |> List.filter (fun c -> builderSet.Contains c.Sha)
        // Fold over builder commits in time order, carrying the blob cache and
        // the per-path requirement references (only changed files rescanned).
        let folder (st: FoldState) (c: CommitInfo) : FoldState =
            let cache, refs = st.Cache, st.Refs
            let prevMap, prevImpl, prevClasses, prevLiterals = st.PrevMapped, st.PrevImplemented, st.PrevClasses, st.PrevLiterals
            let t = tree repo c.Sha
            let files = changedFiles repo c
            let readP (cache: Blobs) path = read repo cache t path
            // Requirement references: rescan changed source/test files only.
            let scanned (cache: Blobs) =
                files
                |> List.filter (fun (p, _, _, _) -> Regex.IsMatch(p, "^(src|web|tests)/"))
                |> List.fold
                    (fun (cache: Blobs, refs: Map<string, string list>) (p, s, _, _) ->
                        if s = "D" then cache, refs |> Map.remove p
                        else
                            let text, cache = readP cache p
                            let changedSinceBootstrap = bootstrapTree |> Map.tryFind p <> (t |> Map.tryFind p)
                            let ids =
                                match text with
                                | Some txt when changedSinceBootstrap -> idRx.Matches txt |> Seq.map (fun m -> m.Value) |> Seq.distinct |> Seq.toList
                                | _ -> []
                            cache, refs |> Map.add p ids)
                    (cache, refs)
            let cache, refs = scanned cache
            let html, cache = readP cache "web/index.html"
            let viewSources, cache =
                t |> Map.toList |> List.filter (fun (p, _) -> p.StartsWith "src/Causa.App/" && p.EndsWith ".fs")
                |> List.fold (fun (texts, cache) (p, _) -> let x, cache = readP cache p in (Option.toList x @ texts), cache) ([], cache)
            let css, cache = readP cache "web/causa.css"
            let pkg, cache = readP cache "package.json"
            let jsFiles, cache =
                t |> Map.toList |> List.filter (fun (p, _) -> p.StartsWith "web/" && (p.EndsWith ".js" || p.EndsWith ".mjs"))
                |> List.fold (fun (texts, cache) (p, _) -> let x, cache = readP cache p in (Option.toList x @ texts), cache) ([], cache)
            let reqMap, cache = readP cache "requirements/requirement-implementation-map.json"
            let manifest, cache = readP cache "requirements/verification/verification-manifest.json"
            let evidence, cache = readP cache "evidence/requirement-verification/requirement-verification.json"
            let events, cache = readP cache ".ros/events/events.jsonl"
            let queue, cache = readP cache ".ros/work/queue.json"
            let markup = (Option.toList html @ viewSources) |> String.concat "\n"
            let efUsed = efTokensIn markup |> List.filter formaClasses.Contains |> List.distinct |> List.sort
            let custom = classesIn (defaultArg html "") |> List.filter (fun k -> not (k.StartsWith "ef-")) |> List.distinct
            let literals, fallbacks, declared, unknown = css |> Option.map (cssFacts inventory) |> Option.defaultValue ([], 0, [], [])
            let pkgJson = pkg |> Option.bind Json.tryParse
            let dep name = pkgJson |> Option.bind (Json.path [ "dependencies"; name ]) |> Option.bind Json.asString
            let plannedStatus (cache: Blobs) =
                planned
                |> List.fold
                    (fun (rows, cache) (p, owner) ->
                        match t |> Map.tryFind p with
                        | None -> (owner, p, "absent") :: rows, cache
                        | Some id when bootstrapTree |> Map.tryFind p = Some id -> (owner, p, "stub") :: rows, cache
                        | Some _ ->
                            let text, cache = readP cache p
                            (owner, p, (if text |> Option.exists isStub then "stub" else "implemented")) :: rows, cache)
                    ([], cache)
            let plannedRows, cache = plannedStatus cache
            let groups =
                plannedRows
                |> List.groupBy (fun (o, _, _) -> o)
                |> List.sortBy fst
                |> List.map (fun (o, rows) ->
                    let n s = rows |> List.filter (fun (_, _, x) -> x = s) |> List.length
                    jobj [ "group", jstr o; "total", jint rows.Length; "absent", jint (n "absent"); "stub", jint (n "stub"); "implemented", jint (n "implemented") ])
            let implementedPaths = plannedRows |> List.filter (fun (_, _, s) -> s = "implemented") |> List.map (fun (_, p, _) -> p) |> Set.ofList
            let planTestsPresent = planTests |> List.filter (fun (p, _) -> implementedPaths.Contains p) |> List.length
            let mapped =
                reqMap |> Option.bind Json.tryParse |> Option.map (Json.array "mappings" >> List.map (str "requirement") >> List.distinct) |> Option.defaultValue []
            let declaredScope = manifest |> Option.bind Json.tryParse |> Option.bind (Json.str "catalogId")
            let verification =
                evidence
                |> Option.bind Json.tryParse
                |> Option.map (fun e ->
                    jobj
                        [ "catalog", jstr (Json.strAt [ "manifest"; "catalogId" ] e |> Option.defaultValue "")
                          "candidate", jstr (str "candidate" e)
                          "runId", jstr (str "runId" e)
                          "applicable", jint (Json.path [ "summary"; "applicable" ] e |> Option.bind Json.asInt64 |> Option.map int |> Option.defaultValue 0)
                          "verified", jint (Json.path [ "summary"; "verified" ] e |> Option.bind Json.asInt64 |> Option.map int |> Option.defaultValue 0)
                          "failed", jint (Json.path [ "summary"; "failed" ] e |> Option.bind Json.asInt64 |> Option.map int |> Option.defaultValue 0)
                          "notApplicable", jint (Json.path [ "summary"; "notApplicable" ] e |> Option.bind Json.asInt64 |> Option.map int |> Option.defaultValue 0)
                          "accepted", jopt jbool (Json.path [ "acceptance"; "accepted" ] e |> Option.bind Json.asBool)
                          "passingIds", Json.array "requirements" e |> List.filter (fun r -> str "status" r = "PASS") |> List.map (str "id") |> jstrs
                          "notApplicableIds", Json.array "requirements" e |> List.filter (fun r -> str "status" r = "NOT_APPLICABLE") |> List.map (str "id") |> jstrs ])
            let praxis, cache =
                events
                |> Option.map (fun text -> text.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.toList |> List.choose Json.tryParse)
                |> Option.defaultValue []
                |> List.fold
                    (fun (rows, cache) e ->
                        let exe = Json.array "telemetryExecutions" e |> List.choose Json.asString |> List.tryHead
                        let baseline, cache =
                            match exe with
                            | Some x ->
                                let env, cache = readP cache $".ros/executions/{x}/envelope.json"
                                env |> Option.bind Json.tryParse |> Option.bind (Json.str "baselineRevision"), cache
                            | None -> None, cache
                        rows
                        @ [ jobj
                                [ "type", jstr (str "type" e)
                                  "workItem", jstr (str "workItem" e)
                                  "occurredAt", jopt jstr (Json.str "occurredAt" e)
                                  "baselineRevision", jopt jstr baseline ] ],
                        cache)
                    ([], cache)
            let work =
                queue |> Option.bind Json.tryParse |> Option.map (Json.array "items") |> Option.defaultValue []
                |> List.map (fun w -> jobj [ "id", jstr (str "id" w); "title", jstr (str "title" w); "state", jstr (str "status" w) ])
            let markers =
                files
                |> List.filter (fun (p, s, _, _) -> s <> "D" && Regex.IsMatch(p, "^(src|web)/") && not (p.Contains "/tests/"))
                |> List.collect (fun (p, _, _, _) -> fst (readP cache p) |> Option.map (shortcutMarkers p) |> Option.defaultValue [])
            let referenced = refs |> Map.toList |> List.collect snd |> List.distinct |> List.sort
            let factsNode =
                jobj
                    [ "formaStylesheetLinked", jbool (html |> Option.exists (fun h -> Regex.IsMatch(h, "href=\"[^\"]*(forma|design-system)[^\"]*\\.css\"")))
                      "formaClasses", jstrs efUsed
                      "customClassCount", jint custom.Length
                      "cssLiterals", literals |> List.map (fun (s, v) -> jobj [ "selector", jstr s; "value", jstr v ]) |> jarr
                      "cssFallbackLiterals", jint fallbacks
                      "cssDeclaredEfProperties", jstrs declared
                      "cssUnknownEfReferences", jstrs unknown
                      "formaVersion", jopt jstr (dep "@echelon-foundry/design-system")
                      "folioDependency", jbool (dep "@echelon-foundry/print-components" |> Option.isSome)
                      "folioUsed", jbool (efUsed |> List.exists (fun k -> k.StartsWith "ef-print") || (jsFiles @ viewSources) |> List.exists (fun s -> s.Contains "print-components"))
                      "limenKernelBoot", jbool (jsFiles |> List.exists (fun s -> s.Contains "@echelon-foundry/limen" && s.Contains "BrowserKernel"))
                      "limenBoundaryFile", jbool (t.ContainsKey "web/boundary.js")
                      "plannedFiles", jarr groups
                      "planTestFilesPresent", jint planTestsPresent
                      "planTestFilesTotal", jint planTests.Length
                      "referencedRequirements", jstrs referenced
                      "requirementMap", jstrs mapped
                      "declaredVerificationScope", jopt jstr declaredScope
                      "verification", jopt id verification
                      "praxisEvents", jarr praxis
                      "workQueue", jarr work
                      "handoffUpdated", jbool (t |> Map.tryFind "HANDOFF.md" <> (bootstrapTree |> Map.tryFind "HANDOFF.md"))
                      "t0Recorded", jbool (t.ContainsKey "evidence/t0/T0.md")
                      "hiddenRequirementElements", jstrs (html |> Option.map hiddenRequirementElements |> Option.defaultValue [])
                      "inertControls", jstrs (html |> Option.map inertControls |> Option.defaultValue [])
                      "shortcutMarkers", jstrs markers
                      "completionArtifacts", jstrs (sealedPaths |> List.filter t.ContainsKey) ]
            let role =
                if c.Sha = bootstrapSha then "bootstrap"
                elif c.CommittedAt < t0 then "owner"
                elif c.Parents.Length > 1 then "merge"
                else "builder"
            let newlyMapped = mapped |> List.filter (fun m -> not (prevMap.Contains m))
            let implementedNow = plannedRows |> List.filter (fun (_, _, s) -> s = "implemented") |> List.length
            let capabilities =
                if role = "merge" then [] else
                (newlyMapped |> List.map (fun m -> (m + " " + (titleOf |> Map.tryFind m |> Option.defaultValue "")).Trim()))
                @ (if implementedNow > prevImpl && role <> "bootstrap" then [ $"planned v4.1.2 files implemented: {prevImpl} -> {implementedNow}" ] else [])
            let added = efUsed |> List.filter (fun k -> not (List.contains k prevClasses))
            let removed = prevClasses |> List.filter (fun k -> not (List.contains k efUsed))
            let architecture =
                if role = "merge" then [ "merge of a pull request: its changes are those of the merged commits" ] else
                [ if not added.IsEmpty then $"Forma classes added: {Specula.Engine.Observer.Rules.commas added}"
                  if not removed.IsEmpty then $"Forma classes removed: {Specula.Engine.Observer.Rules.commas removed}"
                  if literals.Length <> prevLiterals then $"literal colours in web/causa.css: {prevLiterals} -> {literals.Length}"
                  yield! files |> List.filter (fun (p, _, _, _) -> p.StartsWith ".github/workflows/") |> List.map (fun (p, s, _, _) -> $"workflow {s}: {p}")
                  if files |> List.exists (fun (p, _, _, _) -> p = "package.json") then "package.json changed"
                  if files |> List.exists (fun (p, _, _, _) -> p.StartsWith "web/" && p.EndsWith ".js") then "web entry script changed" ]
            let changedIds =
                refs |> Map.toList |> List.filter (fun (p, _) -> files |> List.exists (fun (q, _, _, _) -> q = p)) |> List.collect snd
            let reqsAffected = (changedIds @ newlyMapped) |> List.distinct |> List.sort
            let branch =
                if mainSet.Contains c.Sha then "main" else builderHeads |> List.filter (fun h -> h <> "main") |> List.tryHead |> Option.defaultValue ""
            let node =
                jobj
                    [ "sha", jstr c.Sha
                      "parents", jstrs c.Parents
                      "branch", jstr branch
                      "subject", jstr c.Subject
                      "role", jstr role
                      "committedAt", jtime c.CommittedAt
                      "remoteVisibleAt", jopt jtime (remoteVisible c.Sha)
                      "files",
                      files
                      |> List.map (fun (p, s, a, d) -> jobj [ "path", jstr p; "status", jstr s; "additions", jint a; "deletions", jint d ])
                      |> jarr
                      "requirementsReferenced", jstrs reqsAffected
                      "testsChanged", jstrs (files |> List.filter (fun (p, _, _, _) -> p.StartsWith "tests/") |> List.map (fun (p, _, _, _) -> p))
                      "capabilities", jstrs capabilities
                      "architectureChanges", jstrs architecture
                      "facts", factsNode ]
            { Cache = cache
              Refs = refs
              PrevMapped = Set.ofList mapped
              PrevImplemented = implementedNow
              PrevClasses = efUsed
              PrevLiterals = literals.Length
              Nodes = node :: st.Nodes }
        let nodes =
            builderCommits
            |> List.fold folder { Cache = Map.empty; Refs = Map.empty; PrevMapped = Set.empty; PrevImplemented = 0; PrevClasses = []; PrevLiterals = 0; Nodes = [] }
            |> fun st -> List.rev st.Nodes
        let testData =
            all
            |> List.filter (fun c -> dataOnly.Contains c.Sha)
            |> List.map (fun c -> jobj [ "sha", jstr c.Sha; "at", jtime c.CommittedAt; "subject", jstr c.Subject ])
        let runs =
            runsRaw
            |> List.map (fun r ->
                let id = Json.int64 "id" r |> Option.defaultValue 0L
                let log = logs |> Map.tryFind id
                let completed = str "status" r = "completed"
                jobj
                    [ "id", jint64 id
                      "workflow", jstr (str "name" r)
                      "event", jstr (str "event" r)
                      "branch", jstr (str "head_branch" r)
                      "headSha", jstr (str "head_sha" r)
                      "createdAt", jstr (str "created_at" r)
                      "completedAt", (if completed then jstr (str "updated_at" r) else jnull)
                      "conclusion", jstr (Json.str "conclusion" r |> Option.defaultValue (str "status" r))
                      "failedStep", jopt jstr (log |> Option.bind (Json.str "failedStep"))
                      "logCollected", jbool log.IsSome
                      "logExcerpt", jstrs (log |> Option.map (fun l -> Json.array "lines" l |> List.choose Json.asString) |> Option.defaultValue [])
                      "url", jstr (str "html_url" r) ])
        let sealedNodes =
            sealedPaths
            |> List.choose (fun p ->
                match lines (gitOr repo [ "log"; "--all"; "--diff-filter=A"; "--format=%H"; "--"; p ] "") with
                | sha :: _ ->
                    let content = gitOr repo [ "show"; $"{sha}:{p}" ] ""
                    Some(
                        jobj
                            [ "path", jstr p
                              "commit", jstr sha
                              "sha256", jstr (sha256Text content)
                              "reason", jstr "The builder's final report. Sealed: the blind replay receives its path and digest only; the content is revealed only to the post-replay evaluation." ]
                    )
                | [] -> None)
        let node =
            jobj
                [ "schema", jstr "specula.observer-timeline/1"
                  "repository", jstr "kemiller2002/indy-init-rehearsal-009"
                  "collected",
                  jobj
                      [ "tool", jstr "specula-observer collect (read-only git: log, ls-tree, cat-file, diff-tree, rev-list, show, for-each-ref)"
                        "scopeDigest", jstr (str "digest" scope)
                        "gitInvocations", jint gitInvocations
                        "elapsedMs", jint64 started.ElapsedMilliseconds
                        "runsSource", jstr (str "source" (readJson runsPath))
                        "logsSource", jstr (str "source" (readJson logsPath)) ]
                  "clock",
                  jobj
                      [ "t0", jtime t0
                        "deadline", jtime deadline
                        "budgetMinutes", jint budget
                        "source", jstr $"evidence/t0/T0.md at {t0Commit.Substring(0, 7)} (recorded before conditor init)" ]
                  "commits", jarr nodes
                  "testDataWrites", jarr testData
                  "runs", jarr runs
                  "sealed", jarr sealedNodes
                  "unobservable", jstrs unobservable ]
        writeJson out node
        eprintfn "timeline: %d builder commits, %d test-data writes, %d runs; %d git reads; %d ms" nodes.Length testData.Length runs.Length gitInvocations started.ElapsedMilliseconds
