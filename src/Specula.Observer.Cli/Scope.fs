namespace Specula.Observer.Cli

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Specula.Engine
open Specula.Observer.Cli.Common

/// Compiles the authoritative scope manifest from the frozen Indy-init
/// packages and the ratified catalog. Nothing here reads the monitored
/// repository: scope is independent of the builder.
module Scope =

    let private str (name: string) (el: JsonElement) = Json.str name el |> Option.defaultValue ""
    let private strs name el = Json.array name el |> List.choose Json.asString

    /// Cut policy of a plan task, from the kickoff manifest's priority slices.
    /// Tasks that implement a cut-last slice are marked so; every other task
    /// implements a never-cut slice or is plan infrastructure.
    let cutPolicyOf (taskId: string) =
        match taskId with
        | "T20" | "T21" -> "cut-last" // github-provider
        | "T28" -> "cut-last" // time-travel
        | _ -> "never-cut"

    /// Design contracts from the v4.1.2 Forma layout contract, each with the
    /// exact Forma classes whose absence violates it. Pattern names map to
    /// classes as "ef-" + pattern (checked against the inventory).
    let contractTable : (string * string * string list * string list) list =
        [ "FL-SHELL", "Workspace shell, navigation and header come from Forma's workspace-shell, navigation-shell, site-header and skip-link patterns (layout contract reuse[0]).",
          [ "UI-014"; "UI-020"; "UI-029"; "PERSIST-033" ], [ "workspace-shell"; "navigation-shell"; "site-header"; "skip-link" ]
          "FL-07", "Save-state indicator: Forma status lozenge naming the state plus freshness in the investigation header.",
          [ "PERSIST-030"; "PERSIST-031" ], [ "status-lozenge"; "freshness" ]
          "FL-08", "Loading and errors: the shell renders before WASM with a Forma skeleton; failures use Forma fault patterns.",
          [ "UI-010"; "WASM-LOAD-001" ], [ "skeleton"; "fault-banner" ]
          "FL-09", "Navigation: two levels with the Forma navigation shell; under 48rem it collapses through Forma's own pattern, never a custom menu.",
          [ "UI-020"; "UI-029" ], [ "navigation-shell" ]
          "FL-TOKENS", "web/causa.css may read any --ef-* property in the inventory and must not declare a new one; colours come only from semantic tokens, never from primitives or literals (tokens.rule; FL-10).",
          [ "UI-016"; "PERSIST-030" ], [] ]

    let build (indyInit: string) (baselineRepo: string option) : Text.Json.Nodes.JsonNode =
        let file rel = Path.Combine(indyInit, rel)
        let commit = (gitOr indyInit [ "rev-parse"; "HEAD" ] "unknown").Trim()
        let sources =
            [ "requirements-compiled/v4/verification-plan.json"
              "requirements-compiled/v4/normalized-requirements.json"
              "requirements-compiled/v4.1.2/execution-dag.json"
              "requirements-compiled/v4.1.2/analysis/forma-layout-contract.json"
              "requirements-compiled/v4.1.2/analysis/forma-0.12.0-inventory.json"
              "requirements-compiled/v4.1.2/agent-file-ownership.json"
              "requirements/catalogs/causa-investigation.requirement-catalog.json"
              "kickoff/indy-init.kickoff.json" ]
        let digests = sources |> List.map (fun s -> s, sha256File (file s))
        let plan = readJson (file sources.[0])
        let normalized = readJson (file sources.[1])
        let dag = readJson (file sources.[2])
        let layout = readJson (file sources.[3])
        let inventory = readJson (file sources.[4])
        let catalog = readJson (file sources.[6])
        let kickoff = readJson (file sources.[7])
        let areaOf =
            Json.array "requirements" normalized
            |> List.map (fun r -> str "source" r, str "category" r)
            |> Map.ofList
        let inventoryClasses = strs "classes" inventory |> Set.ofList
        let contracts =
            contractTable
            |> List.map (fun (id, rule, reqs, patterns) ->
                let classes = patterns |> List.map (fun p -> "ef-" + p)
                let unknown = classes |> List.filter (fun c -> not (inventoryClasses.Contains c))
                if not unknown.IsEmpty then failwith ("contract " + id + " names classes absent from the Forma inventory: " + String.Join(",", unknown))
                id, rule, reqs, classes)
        let contractsFor (reqId: string) =
            contracts |> List.filter (fun (_, _, reqs, _) -> List.contains reqId reqs) |> List.map (fun (id, _, _, _) -> id)
        let frozen =
            Json.array "requirements" plan
            |> List.map (fun r ->
                let id = str "requirement" r
                let obligations = Json.array "obligations" r
                let contractsOf (o: JsonElement) = Json.path [ "acceptanceContract"; "dependencies"; "contracts" ] o |> Option.map Json.asArray |> Option.defaultValue [] |> List.choose Json.asString
                let reqDeps (o: JsonElement) = Json.path [ "acceptanceContract"; "dependencies"; "requirements" ] o |> Option.map Json.asArray |> Option.defaultValue [] |> List.choose Json.asString
                let cats (o: JsonElement) = Json.path [ "acceptanceContract"; "requiredEvidenceCategories" ] o |> Option.map Json.asArray |> Option.defaultValue [] |> List.choose Json.asString
                id, str "title" r, str "priority" r, str "document" r, obligations.Length,
                obligations |> List.collect cats |> List.distinct |> List.sort,
                obligations |> List.collect contractsOf |> List.distinct |> List.sort,
                obligations |> List.collect reqDeps |> List.distinct |> List.filter ((<>) id) |> List.sort,
                obligations |> List.map (fun o -> str "sourceObligation" o, str "text" o))
        let frozenIds = frozen |> List.map (fun (id, _, _, _, _, _, _, _, _) -> id)
        let catalogReqs = Json.array "requirements" catalog
        let allIds = frozenIds @ (catalogReqs |> List.map (str "id"))
        let resolve (cover: string) =
            if cover.StartsWith "regex:" then
                let rx = Regex(cover.Substring 6)
                allIds |> List.filter rx.IsMatch
            elif List.contains cover allIds then [ cover ]
            else []
        let tasks =
            Json.array "tasks" dag
            |> List.map (fun t ->
                let id = str "id" t
                let covers = strs "covers" t
                let resolved = covers |> List.collect resolve |> List.distinct
                id, t, covers, resolved)
        let tasksFor reqId =
            tasks |> List.filter (fun (_, _, _, r) -> List.contains reqId r) |> List.map (fun (id, _, _, _) -> id)
        let requirementNodes =
            (frozen
             |> List.map (fun (id, title, priority, document, n, cats, cts, deps, criteria) ->
                 jobj
                     [ "id", jstr id
                       "set", jstr "frozen-plan"
                       "title", jstr title
                       "mandatory", jbool (priority = "P0")
                       "priority", jstr priority
                       "area", jstr (areaOf |> Map.tryFind id |> Option.defaultValue "unclassified")
                       "document", jstr document
                       "obligations", jint n
                       "mandatoryObligations", jint (if priority = "P0" then n else 0)
                       "evidenceCategories", jstrs cats
                       "tasks", jstrs (tasksFor id)
                       "contracts", jstrs cts
                       "dependsOn", jstrs deps
                       "designContracts", jstrs (contractsFor id)
                       "acceptanceCriteria", criteria |> List.map (fun (o, t) -> jobj [ "obligation", jstr o; "text", jstr t ]) |> jarr
                       "expectedEvidence", jstr "verification-plan bound tests per obligation, judged by the Verification gate (release mode) at the candidate SHA" ]))
            @ (catalogReqs
               |> List.map (fun r ->
                   let id = str "id" r
                   let required = Json.bool "required" r |> Option.defaultValue false
                   let checks = strs "verification" r
                   jobj
                       [ "id", jstr id
                         "set", jstr "content-catalog"
                         "title", jstr (str "name" r)
                         "mandatory", jbool required
                         "priority", jstr (if required then "required" else "optional")
                         "area", jstr ("content:" + str "screen" r)
                         "document", jstr sources.[6]
                         "obligations", jint checks.Length
                         "mandatoryObligations", jint (if required then checks.Length else 0)
                         "evidenceCategories", jstrs checks
                         "tasks", jstrs (tasksFor id)
                         "contracts", jstrs []
                         "dependsOn", jstrs (strs "dependsOn" r)
                         "designContracts", jstrs []
                         "acceptanceCriteria", checks |> List.map (fun c -> jobj [ "obligation", jstr (id + "/" + c); "text", jstr (str "name" r + " (" + str "component" r + "): " + c) ]) |> jarr
                         "sources", Json.array "sources" r |> List.map (fun s -> jstr (str "document" s + "#" + str "requirement" s)) |> jarr
                         "expectedEvidence", jstr "dokimos.requirement-verification/1 from a CI run of the Praxis-compiled catalog manifest" ]))
        let taskNodes =
            tasks
            |> List.map (fun (id, t, covers, resolved) ->
                jobj
                    [ "id", jstr id
                      "title", jstr (str "title" t)
                      "owner", jstr (str "owner" t)
                      "estimateMinutes", jint (Json.int64 "estimateMinutes" t |> Option.map int |> Option.defaultValue 0)
                      "earliestStart", jint (Json.int64 "earliestStart" t |> Option.map int |> Option.defaultValue 0)
                      "latestStart", jint (Json.int64 "latestStart" t |> Option.map int |> Option.defaultValue 0)
                      "dependsOn", jstrs (strs "dependsOn" t)
                      "cutPolicy", jstr (cutPolicyOf id)
                      "coversDeclared", jstrs covers
                      "covers", jstrs resolved ])
        let required =
            Json.array "components" kickoff
            |> List.filter (fun c -> Json.bool "required" c = Some true)
            |> List.map (str "id")
        let baseline =
            baselineRepo
            |> Option.map (fun repo ->
                let head = (gitOr repo [ "rev-parse"; "HEAD" ] "").Trim()
                let files = lines (gitOr repo [ "ls-tree"; "-r"; "--name-only"; "HEAD" ] "")
                let markup =
                    files
                    |> List.filter (fun f -> f = "web/index.html" || (f.StartsWith "src/Causa.App/" && f.EndsWith ".fs"))
                    |> List.map (fun f -> gitOr repo [ "show"; $"HEAD:{f}" ] "")
                    |> String.concat "\n"
                let tokens = Regex.Matches(markup, "\\bef-[a-z0-9-]+") |> Seq.map (fun m -> m.Value) |> Seq.toList
                let classes =
                    tokens
                    |> Seq.filter inventoryClasses.Contains
                    |> Seq.distinct
                    |> Seq.sort
                    |> Seq.toList
                jobj
                    [ "repository", jstr "kemiller2002/indy-init-rehearsal-008"
                      "commit", jstr head
                      "formaClasses", jstrs classes
                      "folioUsed", jbool (tokens |> List.exists (fun c -> c.StartsWith "ef-print")) ])
        let conflicts =
            [ jobj
                  [ "id", jstr "SC-1"
                    "description", jstr "Forma version: the v4.1.2 layout contract's inventory is Forma 0.12.0 (tarball sha256 b824568d…), but the T0 bootstrap (Conditor 0.10.20 + requirements-compiler v41 --bootstrap) installs @echelon-foundry/design-system 0.13.0."
                    "sources", jstrs [ "requirements-compiled/v4.1.2/analysis/forma-layout-contract.json#forma"; "bootstrap package.json (generated at T0)" ]
                    "disposition", jstr "Reported, not resolved. The observer checks pattern classes that exist in both 0.12.0 (inventory) and the installed 0.13.0 (all.css), so the conflict does not change any finding; it needs an owner disposition." ]
              jobj
                  [ "id", jstr "SC-2"
                    "description", jstr "Package status: requirements-compiled/v4.1.2/README.md and package-manifest.json say CANDIDATE, while AGENTS.md and R9-READINESS-HANDOFF.md state v4.1.2 is approved (APR-V412-OWNER-3) and frozen at tag indy-init-requirements-v4.1.2."
                    "sources", jstrs [ "requirements-compiled/v4.1.2/README.md"; "AGENTS.md"; "docs/engineering-improvement/R9-READINESS-HANDOFF.md" ]
                    "disposition", jstr "The compiler never writes FROZEN (package-manifest statusMeaning); the tag and the owner's approval record make it frozen. The observer treats v4.1.2 as frozen. No scope is narrowed." ]
              jobj
                  [ "id", jstr "SC-3"
                    "description", jstr "Rehearsal 9 scope wording: OD-INDY-20 says the ten content gaps are 'in scope for Rehearsal 9' and the catalog drives a catalog-only verification workflow; FINAL-READINESS.md ('Unchanged: every v4.1.2 P0 obligation') and the R9 Verification gate (whole frozen v4.1.2 plan) keep the frozen plan in scope. No source says the frozen plan is out of scope."
                    "sources", jstrs [ "docs/OWNER-DECISIONS-2026-11-03.md#OD-INDY-20"; "docs/rehearsal-009-content-readiness/FINAL-READINESS.md"; "tools/r9/verification-gate.yml" ]
                    "disposition", jstr "Not a true conflict: the catalog adds scope; it does not replace the frozen plan. The observer uses the union and does not select the narrower reading. A builder reading OD-INDY-20 as the whole scope is reported as scope narrowing." ] ]
        let body =
            [ "schema", jstr "specula.observer-scope-manifest/1"
              "description", jstr "Authoritative scope for observing Indy Init Rehearsal 009: the frozen v4.1.2 plan (its v4 verification plan's P0 requirements, its execution DAG and Forma layout contract) plus the ratified CAT-CAUSA-INVESTIGATION catalog. Compiled from Indy-init alone; never from the builder's repository."
              "indyInitCommit", jstr commit
              "sources", digests |> List.map (fun (s, d) -> jobj [ "label", jstr $"kemiller2002/Indy-init@{commit.Substring(0, 7)}:{s}"; "path", jstr s; "sha256", jstr d ]) |> jarr
              "summary",
              jobj
                  [ "frozenPlanRequirements", jint frozen.Length
                    "frozenPlanObligations", jint (frozen |> List.sumBy (fun (_, _, _, _, n, _, _, _, _) -> n))
                    "catalogRequirements", jint catalogReqs.Length
                    "catalogObligations", jint (catalogReqs |> List.sumBy (fun r -> (strs "verification" r).Length))
                    "catalogMandatoryObligations", jint (catalogReqs |> List.filter (fun r -> Json.bool "required" r = Some true) |> List.sumBy (fun r -> (strs "verification" r).Length))
                    "tasks", jint tasks.Length ]
              "requiredComponents", jstrs required
              "requirements", jarr requirementNodes
              "tasks", jarr taskNodes
              "designContracts",
              contracts
              |> List.map (fun (id, rule, reqs, classes) ->
                  jobj
                      [ "id", jstr id
                        "rule", jstr rule
                        "requirements", jstrs reqs
                        "requiredClasses", jstrs classes
                        "source", jstr "requirements-compiled/v4.1.2/analysis/forma-layout-contract.json" ])
              |> jarr
              "formaInventory", jobj [ "version", jstr (str "version" inventory); "customProperties", jstrs (strs "customProperties" inventory @ strs "hookProperties" inventory) ]
              "conflicts", jarr conflicts
              "baseline", jopt id baseline ]
        // The digest covers every field but itself.
        let node = jobj body
        let digest = sha256Text (render node)
        node.AsObject().Insert(1, "digest", jstr digest)
        node
