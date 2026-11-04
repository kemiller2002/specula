namespace Specula.Engine.Observer

open System
open Specula.Engine.Observer.Types

/// Deterministic observation rules OBS-001..OBS-008.
///
/// Each rule reads one snapshot and the authoritative scope, and returns
/// findings. A rule that lacks the evidence it needs returns an uncertainty
/// finding and no conclusion. No rule reads the builder's status messages as
/// scope: scope comes only from the manifest.
module Rules =

    let catalogId = "CAT-CAUSA-INVESTIGATION"
    let stopIdleMinutes = 30
    let pushGraceMinutes = 15

    let private short (sha: string) = sha.Substring(0, min 7 sha.Length)
    let commas (xs: string seq) = String.Join(", ", xs)
    let semis (xs: string seq) = String.Join("; ", xs)
    let spaces (xs: string seq) = String.Join(" ", xs)
    let pipes (xs: string seq) = String.Join("|", xs)
    let private plural n (one: string) (many: string) = if n = 1 then $"{n} {one}" else $"{n} {many}"

    /// Values derived once per snapshot and shared by every rule.
    type Context =
        { Scope: ScopeManifest
          Snapshot: Snapshot
          Head: Commit option
          Facts: TreeFacts option
          Elapsed: int
          Remaining: int
          Mandatory: ScopeRequirement list
          FrozenMandatory: ScopeRequirement list
          CatalogMandatory: ScopeRequirement list
          CodeCommitted: Set<string>
          VerifiedCatalog: Set<string>
          NotApplicable: Set<string>
          VerifiedFrozen: Set<string>
          VerificationCorroborated: bool option
          AppWorkStarted: Commit option
          CompletedRuns: CiRun list
          LastBuilderActivity: DateTimeOffset option
          CompletionSignals: string list }

    let private runIdOf (runId: string) =
        let digits = runId |> Seq.takeWhile Char.IsDigit |> Seq.toArray |> String
        match Int64.TryParse digits with
        | true, v -> Some v
        | _ -> None

    let private isAppPath (path: string) =
        (path.StartsWith "src/" || path.StartsWith "web/") && not (path.EndsWith ".fsproj" || path.EndsWith ".csproj")

    let private isSuccess (r: CiRun) = r.Conclusion = "success"
    let private isFailure (r: CiRun) = r.Conclusion = "failure"

    let context (scope: ScopeManifest) (snapshot: Snapshot) : Context =
        let head = Replay.head snapshot
        let facts = head |> Option.map (fun c -> c.Facts)
        let elapsed = Replay.minuteOf snapshot.Clock snapshot.At
        let remaining = max 0 (int (floor (snapshot.Clock.Deadline - snapshot.At).TotalMinutes))
        let mandatory = scope.Requirements |> List.filter (fun r -> r.Mandatory)
        let frozen = mandatory |> List.filter (fun r -> r.Set = RequirementSet.FrozenPlan)
        let catalog = mandatory |> List.filter (fun r -> r.Set = RequirementSet.ContentCatalog)
        let completed = Replay.completedRuns snapshot
        let codeCommitted =
            facts
            |> Option.map (fun f -> Set.ofList (f.RequirementMap @ f.ReferencedRequirements))
            |> Option.defaultValue Set.empty
        // Builder-committed verification counts only when the CI run it
        // names is visible and succeeded at that moment.
        let corroborated, verifiedCatalog, notApplicable =
            match facts |> Option.bind (fun f -> f.Verification) with
            | None -> None, Set.empty, Set.empty
            | Some v ->
                let ok =
                    runIdOf v.RunId
                    |> Option.exists (fun id -> completed |> List.exists (fun r -> r.Id = id && isSuccess r))
                let counts = ok && v.Catalog = catalogId
                Some ok, (if counts then Set.ofList v.PassingIds else Set.empty), (if counts then Set.ofList v.NotApplicableIds else Set.empty)
        // The frozen plan is verified only through its own release gate.
        let gatePassed =
            completed |> List.exists (fun r -> r.Workflow = "Verification gate" && isSuccess r)
        let verifiedFrozen = if gatePassed then frozen |> List.map (fun r -> r.Id) |> Set.ofList else Set.empty
        let builder = Replay.builderCommits snapshot
        let appWork =
            builder
            |> List.filter (fun c -> c.Role = CommitRole.Builder && c.Files |> List.exists (fun f -> isAppPath f.Path && f.Additions > 0))
            |> List.sortBy (fun c -> c.CommittedAt)
            |> List.tryHead
        let completion =
            [ yield! snapshot.SealedPaths |> List.map (fun p -> $"{p} added (content sealed from the observer)")
              yield!
                  facts
                  |> Option.map (fun f -> f.PraxisEvents |> List.filter (fun e -> e.Type = "work.completed" && not (e.WorkItem.StartsWith "ROS-INSTALL")))
                  |> Option.defaultValue []
                  |> List.map (fun e -> $"Praxis work.completed {e.WorkItem}") ]
        { Scope = scope
          Snapshot = snapshot
          Head = head
          Facts = facts
          Elapsed = elapsed
          Remaining = remaining
          Mandatory = mandatory
          FrozenMandatory = frozen
          CatalogMandatory = catalog
          CodeCommitted = codeCommitted
          VerifiedCatalog = verifiedCatalog
          NotApplicable = notApplicable
          VerifiedFrozen = verifiedFrozen
          VerificationCorroborated = corroborated
          AppWorkStarted = appWork
          CompletedRuns = completed
          LastBuilderActivity = builder |> List.map (fun c -> c.CommittedAt) |> List.sortDescending |> List.tryHead
          CompletionSignals = completion }

    let private finding rule key severity title requirements evidence why action confidence intervention fingerprint =
        { Rule = rule
          Key = key
          Kind = FindingKind.Observation
          Severity = severity
          Title = title
          Requirements = requirements
          Evidence = evidence
          Why = why
          Action = action
          Confidence = confidence
          Intervention = intervention
          Fingerprint = fingerprint }

    let uncertainty rule key title evidence why action =
        { Rule = rule
          Key = key
          Kind = FindingKind.Uncertainty
          Severity = Severity.Informational
          Title = title
          Requirements = []
          Evidence = evidence
          Why = why
          Action = action
          Confidence = Confidence.Low
          Intervention = false
          Fingerprint = title }

    let private ids (rs: ScopeRequirement list) = rs |> List.map (fun r -> r.Id)

    let private byArea (rs: ScopeRequirement list) =
        rs |> List.countBy (fun r -> r.Area) |> List.sortByDescending snd

    let private areaSummary (rs: ScopeRequirement list) =
        byArea rs |> List.map (fun (a, n) -> $"{a} {n}") |> String.concat ", "

    let unverifiedFrozen (ctx: Context) =
        ctx.FrozenMandatory |> List.filter (fun r -> not (ctx.VerifiedFrozen.Contains r.Id))

    let unverifiedCatalog (ctx: Context) =
        ctx.CatalogMandatory |> List.filter (fun r -> not (ctx.VerifiedCatalog.Contains r.Id) && not (ctx.NotApplicable.Contains r.Id))

    // ---- OBS-001 scope coverage ----

    let obs001 (ctx: Context) : Finding list =
        match ctx.Facts with
        | None -> []
        | Some f ->
            let frozenTotal = ctx.FrozenMandatory.Length
            let frozenOpen = unverifiedFrozen ctx
            let catalogTotal = ctx.CatalogMandatory.Length
            let catalogVerified = ctx.CatalogMandatory |> List.filter (fun r -> ctx.VerifiedCatalog.Contains r.Id) |> List.length
            let catalogApplicable = ctx.CatalogMandatory |> List.filter (fun r -> not (ctx.NotApplicable.Contains r.Id)) |> List.length
            let frozenReferenced = ctx.FrozenMandatory |> List.filter (fun r -> ctx.CodeCommitted.Contains r.Id) |> List.length
            let activeItems =
                f.PraxisEvents
                |> List.filter (fun e -> e.Type = "work.started")
                |> List.map (fun e -> e.WorkItem)
                |> List.distinct
            let missionItems = f.WorkQueue |> List.filter (fun w -> w.Title.Contains "Build" && w.State <> "complete")
            let workEvidence =
                [ if not activeItems.IsEmpty then $"Active Praxis work item(s): {commas (activeItems)}"
                  yield!
                      missionItems
                      |> List.map (fun w -> $"Mission work item {w.Id} (\"{w.Title}\") is still '{w.State}'") ]
            let declared =
                match f.DeclaredVerificationScope with
                | Some s when s = catalogId && frozenTotal > 0 ->
                    [ finding
                          "OBS-001"
                          "OBS-001/declared-scope"
                          Severity.Warning
                          $"Declared verification scope is {catalogId} only; the frozen v4.1.2 plan's {frozenTotal} mandatory requirements are outside it"
                          (ids frozenOpen)
                          ([ $"requirements/verification/catalog-pin.json pins {s} as the requirements the application is verified against"
                             $"The authoritative scope has {ctx.Mandatory.Length} mandatory requirements: {frozenTotal} frozen-plan P0 + {catalogTotal} catalog"
                             $"Frozen-plan requirements referenced by any changed source or test: {frozenReferenced} of {frozenTotal}"
                             $"Unaddressed frozen-plan areas: {areaSummary frozenOpen}" ]
                           @ workEvidence)
                          "A verification manifest compiled from the catalog alone can be satisfied while the application the frozen plan specifies is not built. Passing it would not establish completion."
                          "Ask the builder to state which frozen-plan slices it is executing and when, or confirm in writing that the catalog-only scope is authorized. Do not accept catalog verification as application completion."
                          Confidence.High
                          false
                          $"declared={s};frozenRef={frozenReferenced / 10}" ]
                | _ -> []
            let narrowComplete =
                let catalogDone =
                    catalogTotal > 0
                    && (catalogVerified * 100 >= catalogApplicable * 90
                        || (f.Verification |> Option.exists (fun v -> v.Accepted = Some true) && ctx.VerificationCorroborated = Some true))
                if catalogDone && frozenOpen.Length = frozenTotal && frozenTotal > 0 then
                    [ finding
                          "OBS-001"
                          "OBS-001/narrow-catalog-complete"
                          Severity.Critical
                          $"Narrow catalog verified ({catalogVerified}/{catalogApplicable} applicable mandatory) while 0 of {frozenTotal} frozen-plan mandatory requirements are verified"
                          (ids frozenOpen)
                          [ $"""Corroborated catalog verification: {catalogVerified} of {catalogApplicable} applicable mandatory catalog requirements pass ({catalogTotal - catalogApplicable} recorded not applicable) (CI run {f.Verification |> Option.map (fun v -> v.RunId) |> Option.defaultValue "?"})"""
                            "No Verification gate run has passed at any visible commit, so no frozen-plan requirement is verified"
                            $"Planned v4.1.2 test files present: {f.PlanTestFilesPresent} of {f.PlanTestFilesTotal}"
                            $"Unverified frozen-plan mandatory requirements by area: {areaSummary frozenOpen}"
                            $"Time remaining: {ctx.Remaining} min" ]
                          "Completing one requirement catalog is not completion of the application. The authoritative scope has been replaced in practice by a narrower one, while most of the budget remains."
                          $"Intervene now: direct the builder to the frozen plan's unaddressed never-cut slices (kernel, persistence, workspace, AI proposals, factory proof) and keep the deadline at {ctx.Snapshot.Clock.Deadline:u}."
                          Confidence.High
                          true
                          $"catalog={catalogVerified};frozen=0" ]
                else []
            declared @ narrowComplete

    // ---- OBS-002 progress trajectory ----

    let private scopeIds (ctx: Context) = ctx.Scope.Requirements |> List.map (fun r -> r.Id) |> Set.ofList

    /// A task is due when its latest start (critical-path schedule) has
    /// passed. It is addressed when the bootstrap ran (T10) or a changed
    /// file references one of the requirements it covers.
    let tasksDue (ctx: Context) =
        let known = scopeIds ctx
        let bootstrapped = ctx.Snapshot.Commits |> List.exists (fun c -> c.Role = CommitRole.Bootstrap)
        ctx.Scope.Tasks
        |> List.filter (fun t -> t.LatestStart <= ctx.Elapsed)
        |> List.map (fun t ->
            let measurable = t.Covers |> List.filter known.Contains
            let addressed =
                (t.Id = "T10" && bootstrapped) || (measurable |> List.exists ctx.CodeCommitted.Contains)
            t, measurable, addressed)

    let progress (ctx: Context) : Progress =
        let f = ctx.Facts
        let planned = f |> Option.map (fun f -> f.PlannedFiles) |> Option.defaultValue []
        let builder = Replay.builderCommits ctx.Snapshot
        let testRuns =
            ctx.CompletedRuns
            |> List.filter (fun r -> r.Conclusion <> "cancelled" && (r.Workflow = "Build and test" || r.Workflow = "Requirement verification" || r.Workflow = "Verification gate"))
        let e2ePassed =
            ctx.CompletedRuns |> List.exists (fun r -> r.Workflow = "Requirement verification" && isSuccess r)
        let mandatoryIds = ctx.Mandatory |> List.map (fun r -> r.Id) |> Set.ofList
        let committed = Set.intersect ctx.CodeCommitted mandatoryIds
        let demonstrated =
            if e2ePassed then
                ctx.CatalogMandatory |> List.filter (fun r -> committed.Contains r.Id) |> List.length
            else 0
        let failedCatalog =
            f
            |> Option.bind (fun f -> f.Verification)
            |> Option.map (fun v -> v.Failed)
            |> Option.defaultValue 0
        let verified = ctx.VerifiedCatalog.Count + ctx.VerifiedFrozen.Count
        let due = tasksDue ctx
        { ElapsedMinutes = ctx.Elapsed
          RemainingMinutes = ctx.Remaining
          BuilderCommits = builder |> List.filter (fun c -> c.Role = CommitRole.Builder) |> List.length
          LastCommit = ctx.Head |> Option.map (fun c -> c.Sha)
          LastCommitAt = ctx.Head |> Option.map (fun c -> c.CommittedAt)
          PlannedFilesImplemented = planned |> List.sumBy (fun g -> g.Implemented)
          PlannedFilesTotal = planned |> List.sumBy (fun g -> g.Total)
          MandatoryRequirements = ctx.Mandatory.Length
          FrozenPlanMandatory = ctx.FrozenMandatory.Length
          CatalogMandatory = ctx.CatalogMandatory.Length
          CodeCommittedRequirements = committed.Count
          DemonstratedRequirements = demonstrated
          VerifiedFrozenPlan = ctx.VerifiedFrozen.Count
          VerifiedCatalog = ctx.VerifiedCatalog |> Set.filter mandatoryIds.Contains |> Set.count
          NotEvaluated = max 0 (ctx.Mandatory.Length - verified - failedCatalog - ctx.NotApplicable.Count)
          TestRunsCompleted = testRuns.Length
          TestRunsPassed = testRuns |> List.filter isSuccess |> List.length
          LiveRoundTripWrites = ctx.Snapshot.TestDataWrites.Length
          TasksDue = due |> List.map (fun (t, _, _) -> t.Id)
          TasksDueUnaddressed = due |> List.filter (fun (_, m, a) -> not a && not m.IsEmpty) |> List.map (fun (t, _, _) -> t.Id) }

    let obs002 (ctx: Context) : Finding list =
        let p = progress ctx
        let due = tasksDue ctx
        let unaddressed =
            due |> List.filter (fun (t, m, a) -> not a && not m.IsEmpty && t.CutPolicy = "never-cut")
        let unmeasurable = due |> List.filter (fun (_, m, _) -> m.IsEmpty) |> List.map (fun (t, _, _) -> t.Id)
        let schedule =
            if unaddressed.IsEmpty || ctx.Facts.IsNone then []
            else
                let reqs = unaddressed |> List.collect (fun (_, m, _) -> m) |> List.distinct
                [ finding
                      "OBS-002"
                      "OBS-002/schedule"
                      Severity.Warning
                      $"""{plural unaddressed.Length "never-cut plan task is" "never-cut plan tasks are"} past their latest start with none of their covered requirements cited by any change"""
                      reqs
                      [ yield!
                            unaddressed
                            |> List.map (fun (t, m, _) -> $"{t.Id} \"{t.Title}\" (owner {t.Owner}; latest start T+{t.LatestStart}; covers {m.Length} scope requirements; none referenced by any changed file)")
                        $"Elapsed {p.ElapsedMinutes} min, remaining {p.RemainingMinutes} min"
                        $"Code committed: {p.CodeCommittedRequirements} mandatory requirements referenced; planned v4.1.2 files implemented {p.PlannedFilesImplemented} of {p.PlannedFilesTotal}"
                        $"Requirements verified: {p.VerifiedCatalog} catalog, {p.VerifiedFrozenPlan} frozen plan; not evaluated: {p.NotEvaluated}"
                        "Measure: requirement IDs cited in changed source and test files. Code that serves a task without citing its requirements is not counted, so this can overstate how far a task is from started."
                        if not unmeasurable.IsEmpty then $"Not measurable from requirement references (excluded): {commas (unmeasurable)}" ]
                      "The frozen execution plan orders never-cut slices on a 334-minute critical path. Slices whose requirements are not being worked by their latest start cannot finish by the deadline without re-planning."
                      "Ask the builder for its plan against the frozen task list (T10..T36) and which of these tasks it will begin next."
                      Confidence.Medium
                      false
                      $"unaddressed>={unaddressed.Length / 3 * 3}" ]
        let verified =
            if p.VerifiedCatalog + p.VerifiedFrozenPlan = 0 then []
            else
                [ finding
                        "OBS-002"
                        "OBS-002/verified"
                        Severity.Informational
                        $"Verified requirements: {p.VerifiedCatalog} catalog, {p.VerifiedFrozenPlan} frozen plan (of {p.MandatoryRequirements} mandatory)"
                        (ctx.VerifiedCatalog |> Set.toList)
                        [ "Counted only from verification evidence whose CI run is visible and succeeded"
                          $"Code committed {p.CodeCommittedRequirements}, demonstrated {p.DemonstratedRequirements}, verified {p.VerifiedCatalog + p.VerifiedFrozenPlan}, not evaluated {p.NotEvaluated}" ]
                        "Progress is measured by verified requirements, not by commits or lines."
                        "None."
                        Confidence.High
                        false
                        $"{p.VerifiedCatalog}/{p.VerifiedFrozenPlan}" ]
        let noCi =
            let firstApp = ctx.AppWorkStarted
            match firstApp with
            | Some c when ctx.Snapshot.Runs.IsEmpty && (ctx.Snapshot.At - c.CommittedAt).TotalMinutes >= 20.0 ->
                [ uncertainty
                      "OBS-002"
                      "OBS-002/no-ci"
                      "No CI evidence for the application commits yet"
                      [ $"First application commit {short c.Sha} at {c.CommittedAt:u}; no workflow run is visible"
                        "Commits may not yet be pushed, or CI may not be configured; neither is observable" ]
                      "Without CI the observer cannot tell whether the code builds or its tests pass. No failure is concluded."
                      "Check that the branch is pushed and CI is running." ]
            | _ -> []
        schedule @ verified @ noCi

    // ---- OBS-003 architecture compliance ----

    let violatedContracts (scope: ScopeManifest) (f: TreeFacts) =
        let used = Set.ofList f.FormaClasses
        scope.DesignContracts
        |> List.choose (fun c ->
            if c.RequiredClasses.IsEmpty then
                // Token contract: colours only from semantic tokens.
                let problems =
                    [ if not f.CssLiterals.IsEmpty then
                          let examples = f.CssLiterals |> List.truncate 4 |> List.map (fun l -> $"{l.Selector} {{ {l.Value} }}")
                          $"{f.CssLiterals.Length} literal colour value(s) outside var() fallbacks, e.g. {semis (examples)}"
                      if not f.CssDeclaredEfProperties.IsEmpty then $"declares Forma properties: {commas (f.CssDeclaredEfProperties)}"
                      if not f.CssUnknownEfReferences.IsEmpty then $"reads --ef-* properties absent from the inventory: {commas (f.CssUnknownEfReferences)}" ]
                if problems.IsEmpty then None else Some(c, problems)
            else
                let missing = c.RequiredClasses |> List.filter (fun k -> not (used.Contains k))
                if missing.IsEmpty then None else Some(c, [ $"missing Forma pattern classes: {commas (missing)}" ]))

    let private markupTouched (ctx: Context) =
        ctx.Snapshot.Commits
        |> List.exists (fun c -> c.Role = CommitRole.Builder && c.Files |> List.exists (fun f -> f.Path.StartsWith "web/"))

    let obs003 (ctx: Context) : Finding list =
        match ctx.Facts with
        | Some f when markupTouched ctx ->
            let violated = violatedContracts ctx.Scope f
            let design =
                if violated.IsEmpty then []
                else
                    let baseline =
                        match ctx.Scope.Baseline with
                        | Some b ->
                            let theirs = Set.ofList b.FormaClasses
                            let lost =
                                violated
                                |> List.collect (fun (c, _) -> c.RequiredClasses)
                                |> List.filter theirs.Contains
                                |> List.distinct
                            if lost.IsEmpty then []
                            else [ $"Regression against {b.Repository}@{short b.Commit}, which used: {commas (lost)}" ]
                        | None -> []
                    [ finding
                          "OBS-003"
                          "OBS-003/design-contract"
                          Severity.Warning
                          (if f.FormaStylesheetLinked then
                               $"""Forma is imported but {plural violated.Length "mandatory layout contract is" "mandatory layout contracts are"} not met"""
                           else $"""{plural violated.Length "mandatory layout contract is" "mandatory layout contracts are"} not met""")
                          (violated |> List.collect (fun (c, _) -> c.Requirements) |> List.distinct)
                          ([ $"""Forma stylesheet linked: {f.FormaStylesheetLinked}; Forma classes used in markup: {(if f.FormaClasses.IsEmpty then "none" else commas (f.FormaClasses))}"""
                             $"Custom (non-Forma) classes in markup: {f.CustomClassCount}" ]
                           @ (violated |> List.map (fun (c, problems) -> $"{c.Id} ({c.Source}): {spaces (problems)}"))
                           @ baseline)
                          "Importing Forma does not satisfy the layout contract. The frozen package requires the named Forma patterns (shell, navigation, save-state lozenge, loading skeleton, fault banner) and semantic colour tokens; custom CSS layout bypasses the tested accessibility and responsiveness those patterns carry."
                          "Ask the builder to adopt the named Forma patterns in web/index.html before adding more screens, and to remove literal colours. Custom CSS that only composes tokens is not itself a violation."
                          Confidence.High
                          false
                          (violated |> List.map (fun (c, _) -> c.Id) |> List.sort |> String.concat ",") ]
            let limen =
                if f.LimenKernelBoot then
                    if f.LimenBoundaryFile then []
                    else
                        [ finding
                              "OBS-003"
                              "OBS-003/limen-boundary-file"
                              Severity.Informational
                              "Limen kernel is booted, but not from the planned web/boundary.js"
                              []
                              [ "web/main.js imports BrowserKernel from @echelon-foundry/limen and starts the WebAssembly engine"
                                "agent-file-ownership.json plans web/boundary.js (integration) as the boundary file; it is absent" ]
                              "The runtime integration is present; only the planned file location differs. Low impact."
                              "None required; note for the integration review."
                              Confidence.High
                              false
                              "main.js" ]
                else
                    [ finding
                          "OBS-003"
                          "OBS-003/limen"
                          Severity.Warning
                          "Application markup changed but no Limen kernel boot is present"
                          [ "WASM-LOAD-001" ]
                          [ "No web entry point imports BrowserKernel from @echelon-foundry/limen" ]
                          "Limen is a required component (kickoff manifest); without it the F# engine is not the application's runtime."
                          "Ask the builder how the engine is hosted."
                          Confidence.Medium
                          false
                          "no-limen" ]
            let folio =
                let required = ctx.Scope.RequiredComponents |> List.contains "folio"
                let reportTask = ctx.Scope.Tasks |> List.tryFind (fun t -> t.Id = "T26")
                match reportTask with
                | Some t when required && not f.FolioUsed && t.LatestStart <= ctx.Elapsed ->
                    [ finding
                          "OBS-003"
                          "OBS-003/folio"
                          Severity.Warning
                          "Folio is required but not integrated, and its plan task is past its latest start"
                          t.Covers
                          [ $"Folio dependency in package.json: {f.FolioDependency}; Folio print components used: {f.FolioUsed}"
                            $"{t.Id} \"{t.Title}\" latest start T+{t.LatestStart}" ]
                          "Folio renders the investigation and engineering proof reports the kickoff manifest requires."
                          "Ask the builder when the report preview and Folio export will be built."
                          Confidence.High
                          false
                          "folio-missing" ]
                | _ -> []
            design @ limen @ folio
        | _ -> []

    // ---- OBS-004 premature completion ----

    let obs004 (ctx: Context) : Finding list =
        let frozenOpen = unverifiedFrozen ctx
        let catalogOpen = unverifiedCatalog ctx
        let openMandatory = frozenOpen @ catalogOpen
        let idle =
            ctx.LastBuilderActivity |> Option.map (fun t -> int (floor (ctx.Snapshot.At - t).TotalMinutes))
        let runsInFlight = ctx.Snapshot.Runs |> List.exists (fun r -> not r.Completed)
        let violated = ctx.Facts |> Option.map (violatedContracts ctx.Scope) |> Option.defaultValue []
        let folioMissing =
            (ctx.Scope.RequiredComponents |> List.contains "folio")
            && ctx.Facts |> Option.exists (fun f -> not f.FolioUsed)
        let unfinished =
            [ $"Unverified mandatory requirements: {openMandatory.Length} of {ctx.Mandatory.Length} ({frozenOpen.Length} frozen plan, {catalogOpen.Length} catalog)"
              $"Unverified frozen-plan areas: {areaSummary frozenOpen}"
              if not violated.IsEmpty then $"Unmet layout contracts: {commas (violated |> List.map (fun (c, _) -> c.Id))}"
              if folioMissing then "Required component Folio is not integrated"
              $"Time remaining: {ctx.Remaining} of {ctx.Snapshot.Clock.BudgetMinutes} min" ]
        let declared =
            if not ctx.CompletionSignals.IsEmpty && not openMandatory.IsEmpty && ctx.Remaining > 0 then
                [ finding
                      "OBS-004"
                      "OBS-004/completion-declared"
                      Severity.Critical
                      $"Builder declared completion with {openMandatory.Length} mandatory requirements unverified and {ctx.Remaining} minutes remaining"
                      (ids openMandatory)
                      (ctx.CompletionSignals @ unfinished)
                      "A completion report ends the builder's effort. With most mandatory scope unverified and most of the budget left, the run would finish far short of the authoritative scope."
                      "Intervene now: reject completion, keep the original deadline, and direct the builder (or a replacement using the observer's handoff) to the unaddressed mandatory scope."
                      Confidence.High
                      true
                      $"open={openMandatory.Length / 10}" ]
            else []
        let stopped =
            match idle with
            | Some minutes when minutes >= stopIdleMinutes && ctx.Remaining >= stopIdleMinutes && not openMandatory.IsEmpty && not runsInFlight ->
                [ finding
                      "OBS-004"
                      "OBS-004/stopped"
                      Severity.Critical
                      $"Builder has made no commit for {minutes} minutes with {ctx.Remaining} minutes remaining and mandatory scope unfinished"
                      (ids openMandatory)
                      ([ $"Last builder commit at {ctx.LastBuilderActivity.Value:u}; no CI run in progress"
                         "No owner decision authorizing a stop is visible in the repository" ]
                       @ unfinished)
                      "An unexplained stop with time remaining forfeits the remaining budget."
                      "Contact the builder or start a replacement from the observer's handoff without resetting the clock."
                      Confidence.Medium
                      true
                      "stopped" ]
            | _ -> []
        declared @ stopped

    // ---- OBS-005 verification blockers ----

    [<RequireQualifiedAccess>]
    type FailureClass =
        | SuiteInfrastructure
        | ToolchainProvisioning
        | FaultProbeSurvived
        | Unknown

    let classify (run: CiRun) : FailureClass =
        let text = String.Join("\n", run.LogExcerpt)
        if not run.LogCollected then FailureClass.Unknown
        elif text.Contains "produced no result file" || text.Contains "run-suites: REFUSED" then FailureClass.SuiteInfrastructure
        elif text.Contains "exit code 155" || text.Contains "global.json" || text.Contains "SDK" then FailureClass.ToolchainProvisioning
        elif text.Contains "fault probe was not detected" then FailureClass.FaultProbeSurvived
        else FailureClass.Unknown

    /// Whether a failure class implies the application itself is defective.
    let impliesApplicationDefect (c: FailureClass) =
        match c with
        | FailureClass.SuiteInfrastructure
        | FailureClass.ToolchainProvisioning
        | FailureClass.Unknown -> false
        | FailureClass.FaultProbeSurvived -> false

    let obs005 (ctx: Context) : Finding list =
        let latestBy (key: CiRun -> string) =
            ctx.CompletedRuns
            |> List.filter (fun r -> r.Conclusion <> "cancelled")
            |> List.groupBy key
            |> List.map (fun (k, rs) -> k, rs |> List.sortBy (fun r -> r.CompletedAt) |> List.last, rs)
        let mode (r: CiRun) = if r.Workflow = "Verification gate" && r.Event = "workflow_dispatch" then "release" else "development"
        latestBy (fun r -> $"{r.Workflow}|{mode r}")
        |> List.collect (fun (_, latest, all) ->
            if not (isFailure latest) then []
            else
                let failures = all |> List.filter isFailure
                let cls = classify latest
                let label = if mode latest = "release" then $"{latest.Workflow} (release)" else latest.Workflow
                let key = $"OBS-005/{label}"
                let sameShaBuild =
                    ctx.CompletedRuns
                    |> List.tryFind (fun r -> r.Workflow = "Build and test" && r.HeadSha = latest.HeadSha && isSuccess r)
                let common =
                    [ $"""{label} run {latest.Id} at {short latest.HeadSha}: {latest.Conclusion} (step: {latest.FailedStep |> Option.defaultValue "not recorded"})"""
                      $"""{plural failures.Length "failed run" "failed runs"} of {all.Length} completed for this workflow""" ]
                    @ (latest.LogExcerpt |> List.truncate 4 |> List.map (fun l -> $"log: {l.Trim()}"))
                match cls with
                | FailureClass.Unknown ->
                    [ uncertainty
                          "OBS-005"
                          key
                          $"{label} failed; cause not determinable from collected evidence"
                          common
                          "The log was not collected or matched no known failure signature. An unexplained failure is not evidence that the application is broken."
                          "Read the job log before drawing a conclusion." ]
                | FailureClass.SuiteInfrastructure ->
                    [ finding
                          "OBS-005"
                          key
                          Severity.Warning
                          (if latest.LogExcerpt |> List.exists (fun l -> l.Contains "produced no result file") then $"{label} is blocked: its suite runner produced no result file"
                           else $"{label} is blocked: its suite runner refused (cause line not in the collected log tail)")
                          []
                          (common
                           @ [ match sameShaBuild with
                               | Some b -> $"Build and test passed at the same commit (run {b.Id}): the application builds and its own tests ran"
                               | None -> "No passing Build and test run at the same commit is visible" ])
                          "The gate refused before judging any obligation. This is a verification-infrastructure failure (suite command or missing plan tests), not proof that the application is defective; but while it persists, no frozen-plan requirement can be verified."
                          "Have the builder open the suite run.log from the gate artifact and determine whether the plan's suite command is broken (owner decision needed to change frozen inputs) or the plan's test projects are missing (builder work)."
                          (if sameShaBuild.IsSome && latest.LogExcerpt |> List.exists (fun l -> l.Contains "produced no result file") then Confidence.High else Confidence.Medium)
                          false
                          "infra" ]
                | FailureClass.ToolchainProvisioning ->
                    [ finding
                          "OBS-005"
                          key
                          Severity.Warning
                          $"{label} failed provisioning the pinned toolchain"
                          []
                          common
                          "The job failed before building: the pinned .NET SDK was not installed. This is CI configuration, not an application defect."
                          "Install the global.json SDK pin in the workflow."
                          Confidence.Medium
                          false
                          "toolchain" ]
                | FailureClass.FaultProbeSurvived ->
                    [ finding
                          "OBS-005"
                          key
                          Severity.Warning
                          $"{label} failed: a content fault probe was not detected"
                          []
                          common
                          "A surviving fault means a test passes against a deliberately broken application: the verification is weaker than it claims. It does not by itself show the shipped application is wrong."
                          "Strengthen the test that should catch the probed fault before relying on its verdict."
                          Confidence.High
                          false
                          "probe" ])

    // ---- OBS-006 Praxis sequencing ----

    let obs006 (ctx: Context) : Finding list =
        match ctx.AppWorkStarted, ctx.Facts with
        | Some first, Some f ->
            let started =
                f.PraxisEvents
                |> List.filter (fun e -> e.Type = "work.started" && not (e.WorkItem.StartsWith "ROS-INSTALL"))
                |> List.sortBy (fun e -> e.OccurredAt)
            let contract = "BOOTSTRAP.md §4: \"Before meaningful repository mutation, establish attributable intent with ./praxis work begin\""
            match started with
            | [] ->
                [ finding
                      "OBS-006"
                      "OBS-006/sequencing"
                      Severity.Warning
                      "Application implementation began with no Praxis work item registered"
                      []
                      [ $"First application commit {short first.Sha} at {first.CommittedAt:u} changes {first.Files |> List.filter (fun x -> isAppPath x.Path) |> List.length} application files"
                        "No work.started event other than the ROS installation exists in .ros/events/events.jsonl"
                        contract ]
                      "Unregistered work has no attributable intent, allowed actions or required evidence; a successor cannot take it over through Praxis."
                      "Ask the builder to run ./praxis work begin for the mission work item now."
                      Confidence.High
                      false
                      "unregistered" ]
            | e :: _ ->
                match e.OccurredAt with
                | Some at when at > first.CommittedAt ->
                    let late = int (floor (at - first.CommittedAt).TotalMinutes)
                    let baseline =
                        match e.BaselineRevision with
                        | Some b -> $"The execution's baseline revision is {short b}, so earlier implementation commits are outside the recorded execution"
                        | None -> "No baseline revision recorded"
                    [ finding
                          "OBS-006"
                          "OBS-006/sequencing"
                          Severity.Warning
                          $"Praxis work item {e.WorkItem} was registered {late} minutes after application implementation began"
                          []
                          [ $"First application commit {short first.Sha} at {first.CommittedAt:u}"
                            $"work.started {e.WorkItem} at {at:u}"
                            baseline
                            contract ]
                          "Work done before registration is not attributed to the work item's execution record."
                          "Record the deviation; ensure further work stays inside a registered item."
                          Confidence.High
                          false
                          "late" ]
                | _ -> []
        | _ -> []

    // ---- OBS-007 functional shortcuts ----

    let obs007 (ctx: Context) : Finding list =
        match ctx.Facts with
        | None -> []
        | Some f ->
            let item key title (evidence: string list) why =
                if evidence.IsEmpty then []
                else
                    [ finding "OBS-007" key Severity.Warning title [] (evidence |> List.truncate 12) why "Inspect the listed elements and ask the builder to wire or remove them." Confidence.Medium false (string evidence.Length) ]
            let claims =
                Replay.builderCommits ctx.Snapshot
                |> List.filter (fun c ->
                    let s = c.Subject.ToLowerInvariant()
                    (s.Contains "verified" || s.Contains "accepted") && Text.RegularExpressions.Regex.IsMatch(s, "\\d+\\s*/\\s*\\d+"))
                |> List.filter (fun c ->
                    // Supported when a successful verification run completed
                    // before the claim at a commit the claim is about.
                    not (ctx.CompletedRuns
                         |> List.exists (fun r -> r.Workflow = "Requirement verification" && isSuccess r && r.CompletedAt |> Option.exists (fun t -> t <= c.CommittedAt))))
                |> List.map (fun c -> $"{short c.Sha}: \"{c.Subject}\" has no successful verification run completed before it")
            item "OBS-007/hidden-fields" "Requirement-mapped elements are hidden" f.HiddenRequirementElements "Hidden elements can satisfy field-ID checks without a usable control."
            @ item "OBS-007/inert-controls" "Input controls with no event or binding" f.InertControls "An input that dispatches nothing cannot change state."
            @ item "OBS-007/shortcut-markers" "Placeholder or mock markers in application source" f.ShortcutMarkers "Placeholders presented as implemented features overstate completion."
            @ item "OBS-007/unsupported-claims" "Completion claims without corroborating CI evidence" claims "A claim of verification must be backed by an executed run."

    // ---- OBS-008 recovery readiness ----

    /// Whether a commit was pushed by `at`, judged only from CI runs created
    /// by then on the commit or a descendant.
    let pushedBy (snapshot: Snapshot) (commit: Commit) =
        let byChild =
            snapshot.Commits
            |> List.collect (fun c -> c.Parents |> List.map (fun p -> p, c.Sha))
            |> List.groupBy fst
            |> List.map (fun (p, cs) -> p, cs |> List.map snd)
            |> Map.ofList
        let rec descendants (frontier: string list) (seen: Set<string>) =
            match frontier with
            | [] -> seen
            | x :: rest ->
                let children = byChild |> Map.tryFind x |> Option.defaultValue [] |> List.filter (fun c -> not (seen.Contains c))
                descendants (children @ rest) (Set.union seen (Set.ofList children))
        let family = descendants [ commit.Sha ] (Set.singleton commit.Sha)
        snapshot.Runs |> List.exists (fun r -> family.Contains r.Run.HeadSha)

    let recovery (ctx: Context) : Recovery =
        let f = ctx.Facts
        let has pred = f |> Option.exists pred
        let builder = Replay.builderCommits ctx.Snapshot |> List.filter (fun c -> c.Role = CommitRole.Builder)
        // A commit counts as unpushed only after a grace period: CI normally
        // starts within a minute of a push.
        let unpushed =
            builder
            |> List.filter (fun c -> (ctx.Snapshot.At - c.CommittedAt).TotalMinutes >= float pushGraceMinutes && not (pushedBy ctx.Snapshot c))
        let checks =
            [ { Item = "Authoritative T0 and deadline recorded"
                Present = has (fun f -> f.T0Recorded)
                Detail = "evidence/t0/T0.md" }
              { Item = "Active Praxis work item"
                Present = has (fun f -> f.PraxisEvents |> List.exists (fun e -> e.Type = "work.started" && not (e.WorkItem.StartsWith "ROS-INSTALL")))
                Detail = ".ros/events/events.jsonl work.started" }
              { Item = "Continuation record (next action)"
                Present = has (fun f -> f.HandoffUpdated || f.PraxisEvents |> List.exists (fun e -> e.Type = "work.checkpoint"))
                Detail = "HANDOFF.md updated since bootstrap, or a Praxis work checkpoint" }
              { Item = "Declared scope and verification manifest"
                Present = has (fun f -> f.DeclaredVerificationScope.IsSome)
                Detail = "requirements/verification/catalog-pin.json" }
              { Item = "CI status available"
                Present = not ctx.CompletedRuns.IsEmpty
                Detail = "at least one completed workflow run" }
              { Item = "Builder commits pushed"
                Present = unpushed.IsEmpty
                Detail =
                  if unpushed.IsEmpty then "every visible builder commit has CI evidence of a push"
                  else
                      let shas: string list = unpushed |> List.map (fun c -> short c.Sha)
                      $"{unpushed.Length} commit(s) without push evidence: {commas (shas)}" } ]
        let missing = checks |> List.filter (fun c -> not c.Present) |> List.map (fun c -> c.Item)
        let readiness =
            if missing.IsEmpty then Readiness.Ready
            elif missing.Length >= 3 then Readiness.Insufficient
            else Readiness.Partial
        { Readiness = readiness; Checks = checks; Missing = missing }

    let obs008 (ctx: Context) : Finding list =
        let r = recovery ctx
        let builderActive = Replay.builderCommits ctx.Snapshot |> List.exists (fun c -> c.Role = CommitRole.Builder)
        if not builderActive || r.Readiness = Readiness.Ready then []
        else
            let stopSignal = not ctx.CompletionSignals.IsEmpty
            let severity =
                match r.Readiness with
                | Readiness.Insufficient when stopSignal -> Severity.Critical
                | Readiness.Insufficient -> Severity.Warning
                | _ -> Severity.Informational
            [ finding
                  "OBS-008"
                  "OBS-008/recovery"
                  severity
                  $"Recovery readiness {readinessCode r.Readiness}: a replacement builder would lack {commas (r.Missing)}"
                  []
                  (r.Checks |> List.map (fun c -> $"""{(if c.Present then "present" else "MISSING")}: {c.Item} ({c.Detail})"""))
                  "If the builder stopped now, a successor must reconstruct intent, scope and next action from the repository alone."
                  "Ask the builder to push, record a Praxis checkpoint with the next action, and keep HANDOFF.md current."
                  Confidence.High
                  (severity = Severity.Critical)
                  $"{readinessCode r.Readiness}:{pipes (r.Missing)}" ]

    // ---- run all ----

    let sessionUncertainty (ctx: Context) : Finding list =
        if ctx.Snapshot.Unobservable.IsEmpty then []
        else
            [ uncertainty
                  "OBS-000"
                  "OBS-000/unobservable"
                  "Builder session activity between commits is not observable"
                  ctx.Snapshot.Unobservable
                  "Only repository and CI evidence is available; no conclusion is drawn about unrecorded work."
                  "None; findings are limited to what the repository shows." ]

    let evaluate (scope: ScopeManifest) (snapshot: Snapshot) : Context * Finding list =
        let ctx = context scope snapshot
        ctx,
        [ sessionUncertainty ctx
          obs001 ctx
          obs002 ctx
          obs003 ctx
          obs004 ctx
          obs005 ctx
          obs006 ctx
          obs007 ctx
          obs008 ctx ]
        |> List.concat
