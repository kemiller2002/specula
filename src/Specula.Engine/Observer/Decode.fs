namespace Specula.Engine.Observer

open System
open System.Text.Json
open Specula.Engine
open Specula.Engine.Observer.Types

/// Total decoders for the scope manifest and the timeline. Input is
/// untrusted data. A missing optional field reads as absent, never as zero
/// or success; a missing required field rejects the document.
module Decode =

    let scopeSchema = "specula.observer-scope-manifest/1"
    let timelineSchema = "specula.observer-timeline/1"

    let private strs name el = Json.array name el |> List.choose Json.asString
    let private intOr name fallback el = Json.int64 name el |> Option.map int |> Option.defaultValue fallback
    let private boolOr name fallback el = Json.bool name el |> Option.defaultValue fallback
    let private strOr name fallback el = Json.str name el |> Option.defaultValue fallback

    let private required (what: string) (value: 'a option) : Result<'a, string> =
        match value with
        | Some v -> Ok v
        | None -> Error $"missing {what}"

    let private sequence (results: Result<'a, string> list) : Result<'a list, string> =
        List.foldBack
            (fun r acc ->
                match r, acc with
                | Ok v, Ok vs -> Ok(v :: vs)
                | Error e, _ -> Error e
                | _, Error e -> Error e)
            results
            (Ok [])

    let private set code =
        match code with
        | "frozen-plan" -> Some RequirementSet.FrozenPlan
        | "content-catalog" -> Some RequirementSet.ContentCatalog
        | _ -> None

    let requirement (el: JsonElement) : Result<ScopeRequirement, string> =
        match Json.str "id" el, Json.str "set" el |> Option.bind set with
        | Some id, Some s ->
            Ok
                { Id = id
                  Set = s
                  Title = strOr "title" "" el
                  Mandatory = boolOr "mandatory" false el
                  Priority = strOr "priority" "unstated" el
                  Area = strOr "area" "unclassified" el
                  Document = strOr "document" "" el
                  Obligations = intOr "obligations" 0 el
                  MandatoryObligations = intOr "mandatoryObligations" 0 el
                  EvidenceCategories = strs "evidenceCategories" el
                  Tasks = strs "tasks" el
                  Contracts = strs "contracts" el
                  DependsOn = strs "dependsOn" el
                  DesignContracts = strs "designContracts" el }
        | None, _ -> Error "requirement without id"
        | Some id, None -> Error $"requirement {id} has no known set"

    let task (el: JsonElement) : Result<PlanTask, string> =
        Json.str "id" el
        |> required "task id"
        |> Result.map (fun id ->
            { Id = id
              Title = strOr "title" "" el
              Owner = strOr "owner" "" el
              EstimateMinutes = intOr "estimateMinutes" 0 el
              EarliestStart = intOr "earliestStart" 0 el
              LatestStart = intOr "latestStart" (intOr "earliestStart" 0 el) el
              DependsOn = strs "dependsOn" el
              CutPolicy = strOr "cutPolicy" "unstated" el
              Covers = strs "covers" el })

    let contract (el: JsonElement) : Result<DesignContract, string> =
        Json.str "id" el
        |> required "design contract id"
        |> Result.map (fun id ->
            { Id = id
              Rule = strOr "rule" "" el
              Requirements = strs "requirements" el
              RequiredClasses = strs "requiredClasses" el
              Source = strOr "source" "" el })

    let conflict (el: JsonElement) : SourceConflict =
        { Id = strOr "id" "" el
          Description = strOr "description" "" el
          Sources = strs "sources" el
          Disposition = strOr "disposition" "" el }

    let scope (text: string) : Result<ScopeManifest, string> =
        match Json.tryParse text with
        | None -> Error "scope manifest is not valid JSON"
        | Some root when Json.str "schema" root <> Some scopeSchema -> Error $"scope manifest schema is not {scopeSchema}"
        | Some root ->
            let requirements = Json.array "requirements" root |> List.map requirement |> sequence
            let tasks = Json.array "tasks" root |> List.map task |> sequence
            let contracts = Json.array "designContracts" root |> List.map contract |> sequence
            match requirements, tasks, contracts with
            | Ok rs, Ok ts, Ok cs ->
                let duplicate = rs |> List.countBy (fun r -> r.Id) |> List.tryFind (fun (_, n) -> n > 1)
                match duplicate with
                | Some(id, _) -> Error $"requirement {id} appears twice"
                | None when rs.IsEmpty -> Error "scope manifest lists no requirements"
                | None ->
                    Ok
                        { Schema = scopeSchema
                          Digest = strOr "digest" "" root
                          Sources = Json.array "sources" root |> List.choose (Json.str "label")
                          Requirements = rs
                          Tasks = ts
                          DesignContracts = cs
                          RequiredComponents = strs "requiredComponents" root
                          Conflicts = Json.array "conflicts" root |> List.map conflict
                          Baseline =
                            Json.prop "baseline" root
                            |> Option.map (fun b ->
                                { Repository = strOr "repository" "" b
                                  Commit = strOr "commit" "" b
                                  FormaClasses = strs "formaClasses" b
                                  FolioUsed = boolOr "folioUsed" false b }) }
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e

    // ---- timeline ----

    let private changedFile (el: JsonElement) : ChangedFile =
        { Path = strOr "path" "" el
          Status = strOr "status" "" el
          Additions = intOr "additions" 0 el
          Deletions = intOr "deletions" 0 el }

    let private fileGroup (el: JsonElement) : FileGroup =
        { Group = strOr "group" "" el
          Total = intOr "total" 0 el
          Absent = intOr "absent" 0 el
          Stub = intOr "stub" 0 el
          Implemented = intOr "implemented" 0 el }

    let private praxisEvent (el: JsonElement) : PraxisEvent =
        { Type = strOr "type" "" el
          WorkItem = strOr "workItem" "" el
          OccurredAt = Json.time "occurredAt" el
          BaselineRevision = Json.str "baselineRevision" el }

    let private verification (el: JsonElement) : VerificationEvidence =
        { Catalog = strOr "catalog" "" el
          Candidate = strOr "candidate" "" el
          RunId = strOr "runId" "" el
          Applicable = intOr "applicable" 0 el
          Verified = intOr "verified" 0 el
          Failed = intOr "failed" 0 el
          NotApplicable = intOr "notApplicable" 0 el
          Accepted = Json.bool "accepted" el
          PassingIds = strs "passingIds" el
          NotApplicableIds = strs "notApplicableIds" el }

    let facts (el: JsonElement) : TreeFacts =
        { FormaStylesheetLinked = boolOr "formaStylesheetLinked" false el
          FormaClasses = strs "formaClasses" el
          CustomClassCount = intOr "customClassCount" 0 el
          CssLiterals =
            Json.array "cssLiterals" el
            |> List.map (fun l -> { Selector = strOr "selector" "" l; Value = strOr "value" "" l })
          CssFallbackLiterals = intOr "cssFallbackLiterals" 0 el
          CssDeclaredEfProperties = strs "cssDeclaredEfProperties" el
          CssUnknownEfReferences = strs "cssUnknownEfReferences" el
          FormaVersion = Json.str "formaVersion" el
          FolioDependency = boolOr "folioDependency" false el
          FolioUsed = boolOr "folioUsed" false el
          LimenKernelBoot = boolOr "limenKernelBoot" false el
          LimenBoundaryFile = boolOr "limenBoundaryFile" false el
          PlannedFiles = Json.array "plannedFiles" el |> List.map fileGroup
          PlanTestFilesPresent = intOr "planTestFilesPresent" 0 el
          PlanTestFilesTotal = intOr "planTestFilesTotal" 0 el
          ReferencedRequirements = strs "referencedRequirements" el
          RequirementMap = strs "requirementMap" el
          DeclaredVerificationScope = Json.str "declaredVerificationScope" el
          Verification = Json.prop "verification" el |> Option.map verification
          PraxisEvents = Json.array "praxisEvents" el |> List.map praxisEvent
          WorkQueue =
            Json.array "workQueue" el
            |> List.map (fun w -> { Id = strOr "id" "" w; Title = strOr "title" "" w; State = strOr "state" "" w })
          HandoffUpdated = boolOr "handoffUpdated" false el
          T0Recorded = boolOr "t0Recorded" false el
          HiddenRequirementElements = strs "hiddenRequirementElements" el
          InertControls = strs "inertControls" el
          ShortcutMarkers = strs "shortcutMarkers" el
          CompletionArtifacts = strs "completionArtifacts" el }

    let private role code =
        match code with
        | "bootstrap" -> Some CommitRole.Bootstrap
        | "builder" -> Some CommitRole.Builder
        | "merge" -> Some CommitRole.Merge
        | "owner" -> Some CommitRole.Owner
        | _ -> None

    let commit (el: JsonElement) : Result<Commit, string> =
        match Json.str "sha" el, Json.time "committedAt" el, Json.str "role" el |> Option.bind role, Json.prop "facts" el with
        | Some sha, Some at, Some r, Some f ->
            Ok
                { Sha = sha
                  Parents = strs "parents" el
                  Branch = strOr "branch" "" el
                  Subject = strOr "subject" "" el
                  Role = r
                  CommittedAt = at
                  RemoteVisibleAt = Json.time "remoteVisibleAt" el
                  Files = Json.array "files" el |> List.map changedFile
                  RequirementsReferenced = strs "requirementsReferenced" el
                  TestsChanged = strs "testsChanged" el
                  Capabilities = strs "capabilities" el
                  ArchitectureChanges = strs "architectureChanges" el
                  Facts = facts f }
        | None, _, _, _ -> Error "commit without sha"
        | Some sha, None, _, _ -> Error $"commit {sha} has no committedAt"
        | Some sha, _, None, _ -> Error $"commit {sha} has no known role"
        | Some sha, _, _, None -> Error $"commit {sha} has no facts"

    let run (el: JsonElement) : Result<CiRun, string> =
        match Json.int64 "id" el, Json.time "createdAt" el with
        | Some id, Some created ->
            Ok
                { Id = id
                  Workflow = strOr "workflow" "" el
                  Event = strOr "event" "" el
                  Branch = strOr "branch" "" el
                  HeadSha = strOr "headSha" "" el
                  CreatedAt = created
                  CompletedAt = Json.time "completedAt" el
                  Conclusion = strOr "conclusion" "unknown" el
                  FailedStep = Json.str "failedStep" el
                  LogCollected = boolOr "logCollected" false el
                  LogExcerpt = strs "logExcerpt" el }
        | _ -> Error "CI run without id or createdAt"

    let timeline (text: string) : Result<Timeline, string> =
        match Json.tryParse text with
        | None -> Error "timeline is not valid JSON"
        | Some root when Json.str "schema" root <> Some timelineSchema -> Error $"timeline schema is not {timelineSchema}"
        | Some root ->
            let clock =
                match Json.timeAt [ "clock"; "t0" ] root, Json.timeAt [ "clock"; "deadline" ] root with
                | Some t0, Some deadline ->
                    Ok
                        { T0 = t0
                          Deadline = deadline
                          BudgetMinutes = Json.path [ "clock"; "budgetMinutes" ] root |> Option.bind Json.asInt64 |> Option.map int |> Option.defaultValue 0
                          Source = Json.strAt [ "clock"; "source" ] root |> Option.defaultValue "" }
                | _ -> Error "timeline has no authoritative T0 and deadline"
            match clock, Json.array "commits" root |> List.map commit |> sequence, Json.array "runs" root |> List.map run |> sequence with
            | Ok c, Ok commits, Ok runs ->
                Ok
                    { Schema = timelineSchema
                      Repository = strOr "repository" "" root
                      Clock = c
                      Commits = commits |> List.sortBy (fun x -> x.CommittedAt)
                      TestDataWrites =
                        Json.array "testDataWrites" root
                        |> List.choose (fun w ->
                            match Json.str "sha" w, Json.time "at" w with
                            | Some sha, Some at -> Some { Sha = sha; At = at; Subject = strOr "subject" "" w }
                            | _ -> None)
                      Runs = runs |> List.sortBy (fun r -> r.CreatedAt)
                      // Only the path, commit and digest of a sealed artifact
                      // are read; any other field (such as content) is ignored.
                      Sealed =
                        Json.array "sealed" root
                        |> List.map (fun s ->
                            { Path = strOr "path" "" s
                              Commit = strOr "commit" "" s
                              Sha256 = strOr "sha256" "" s
                              Reason = strOr "reason" "" s })
                      Unobservable = strs "unobservable" root }
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
