namespace Specula.Engine.Observer

open System

/// Types of the read-only implementation observer.
///
/// Everything here is immutable data. The observer never holds a handle to
/// the monitored repository: it reads a timeline that a read-only collector
/// produced, and an authoritative scope manifest compiled from the frozen
/// requirements. A finding is computed from the part of the timeline that
/// was observable at one instant, and nothing else.
module Types =

    // ---- authoritative scope (independent of the builder) ----

    [<RequireQualifiedAccess>]
    type RequirementSet =
        | FrozenPlan
        | ContentCatalog

    let requirementSetCode set =
        match set with
        | RequirementSet.FrozenPlan -> "frozen-plan"
        | RequirementSet.ContentCatalog -> "content-catalog"

    type ScopeRequirement =
        { Id: string
          Set: RequirementSet
          Title: string
          Mandatory: bool
          Priority: string
          Area: string
          Document: string
          Obligations: int
          MandatoryObligations: int
          EvidenceCategories: string list
          Tasks: string list
          Contracts: string list
          DependsOn: string list
          DesignContracts: string list }

    type PlanTask =
        { Id: string
          Title: string
          Owner: string
          EstimateMinutes: int
          EarliestStart: int
          LatestStart: int
          DependsOn: string list
          CutPolicy: string
          Covers: string list }

    /// One mandatory design contract with the exact Forma classes whose
    /// absence violates it. A rule is only ever evaluated against markup;
    /// custom CSS on its own is not a violation.
    type DesignContract =
        { Id: string
          Rule: string
          Requirements: string list
          RequiredClasses: string list
          Source: string }

    type SourceConflict =
        { Id: string
          Description: string
          Sources: string list
          Disposition: string }

    type BaselineFacts =
        { Repository: string
          Commit: string
          FormaClasses: string list
          FolioUsed: bool }

    type ScopeManifest =
        { Schema: string
          Digest: string
          Sources: string list
          Requirements: ScopeRequirement list
          Tasks: PlanTask list
          DesignContracts: DesignContract list
          RequiredComponents: string list
          Conflicts: SourceConflict list
          Baseline: BaselineFacts option }

    // ---- historical timeline (what the repository and CI exposed) ----

    type ChangedFile =
        { Path: string
          Status: string
          Additions: int
          Deletions: int }

    type CssLiteral = { Selector: string; Value: string }

    type FileGroup =
        { Group: string
          Total: int
          Absent: int
          Stub: int
          Implemented: int }

    type PraxisEvent =
        { Type: string
          WorkItem: string
          OccurredAt: DateTimeOffset option
          BaselineRevision: string option }

    type WorkItem = { Id: string; Title: string; State: string }

    type VerificationEvidence =
        { Catalog: string
          Candidate: string
          RunId: string
          Applicable: int
          Verified: int
          Failed: int
          NotApplicable: int
          Accepted: bool option
          PassingIds: string list
          NotApplicableIds: string list }

    /// Facts read from the repository tree at one commit by the read-only
    /// collector. Each field is a deterministic extraction; none is a
    /// judgement. Judgements are made by the rules.
    type TreeFacts =
        { FormaStylesheetLinked: bool
          FormaClasses: string list
          CustomClassCount: int
          CssLiterals: CssLiteral list
          CssFallbackLiterals: int
          CssDeclaredEfProperties: string list
          CssUnknownEfReferences: string list
          FormaVersion: string option
          FolioDependency: bool
          FolioUsed: bool
          LimenKernelBoot: bool
          LimenBoundaryFile: bool
          PlannedFiles: FileGroup list
          PlanTestFilesPresent: int
          PlanTestFilesTotal: int
          ReferencedRequirements: string list
          RequirementMap: string list
          DeclaredVerificationScope: string option
          Verification: VerificationEvidence option
          PraxisEvents: PraxisEvent list
          WorkQueue: WorkItem list
          HandoffUpdated: bool
          T0Recorded: bool
          HiddenRequirementElements: string list
          InertControls: string list
          ShortcutMarkers: string list
          CompletionArtifacts: string list }

    [<RequireQualifiedAccess>]
    type CommitRole =
        | Bootstrap
        | Builder
        | Merge
        | Owner

    type Commit =
        { Sha: string
          Parents: string list
          Branch: string
          Subject: string
          Role: CommitRole
          CommittedAt: DateTimeOffset
          RemoteVisibleAt: DateTimeOffset option
          Files: ChangedFile list
          RequirementsReferenced: string list
          TestsChanged: string list
          Capabilities: string list
          ArchitectureChanges: string list
          Facts: TreeFacts }

    /// A write the application under test made to its own data branch
    /// during a live round-trip test. It is evidence that a test ran, not
    /// builder work.
    type TestDataWrite =
        { Sha: string
          At: DateTimeOffset
          Subject: string }

    type CiRun =
        { Id: int64
          Workflow: string
          Event: string
          Branch: string
          HeadSha: string
          CreatedAt: DateTimeOffset
          CompletedAt: DateTimeOffset option
          Conclusion: string
          FailedStep: string option
          LogCollected: bool
          LogExcerpt: string list }

    /// A file whose content is sealed from the blind replay. Only its path,
    /// the commit that added it and a digest are kept in the timeline.
    type SealedArtifact =
        { Path: string
          Commit: string
          Sha256: string
          Reason: string }

    type Clock =
        { T0: DateTimeOffset
          Deadline: DateTimeOffset
          BudgetMinutes: int
          Source: string }

    type Timeline =
        { Schema: string
          Repository: string
          Clock: Clock
          Commits: Commit list
          TestDataWrites: TestDataWrite list
          Runs: CiRun list
          Sealed: SealedArtifact list
          Unobservable: string list }

    // ---- replay ----

    /// Local: the observer reads the builder's commits as they are made
    /// (commit timestamp). Remote: the observer sees only what GitHub shows,
    /// approximated by the first CI run on the commit or a descendant (an
    /// upper bound on push time).
    [<RequireQualifiedAccess>]
    type Visibility =
        | Local
        | Remote

    let visibilityCode v =
        match v with
        | Visibility.Local -> "local"
        | Visibility.Remote -> "remote"

    type RunView =
        { Run: CiRun
          Completed: bool }

    /// Everything the observer may know at one instant.
    type Snapshot =
        { At: DateTimeOffset
          Mode: Visibility
          Clock: Clock
          Commits: Commit list
          TestDataWrites: TestDataWrite list
          Runs: RunView list
          SealedPaths: string list
          Unobservable: string list }

    // ---- findings and alerts ----

    [<RequireQualifiedAccess>]
    type Severity =
        | Critical
        | Warning
        | Informational

    let severityCode s =
        match s with
        | Severity.Critical -> "critical"
        | Severity.Warning -> "warning"
        | Severity.Informational -> "informational"

    let severityRank s =
        match s with
        | Severity.Critical -> 3
        | Severity.Warning -> 2
        | Severity.Informational -> 1

    [<RequireQualifiedAccess>]
    type Confidence =
        | High
        | Medium
        | Low

    let confidenceCode c =
        match c with
        | Confidence.High -> "high"
        | Confidence.Medium -> "medium"
        | Confidence.Low -> "low"

    /// An observation states a conclusion supported by evidence; an
    /// uncertainty states that the evidence needed for a conclusion is
    /// missing, and draws none.
    [<RequireQualifiedAccess>]
    type FindingKind =
        | Observation
        | Uncertainty

    type Finding =
        { Rule: string
          Key: string
          Kind: FindingKind
          Severity: Severity
          Title: string
          Requirements: string list
          Evidence: string list
          Why: string
          Action: string
          Confidence: Confidence
          Intervention: bool
          /// The material evidence. An alert is repeated only when this or
          /// the severity changes.
          Fingerprint: string }

    [<RequireQualifiedAccess>]
    type AlertChange =
        | New
        | Escalated
        | Deescalated
        | EvidenceChanged
        | Resolved

    let alertChangeCode c =
        match c with
        | AlertChange.New -> "new"
        | AlertChange.Escalated -> "escalated"
        | AlertChange.Deescalated -> "de-escalated"
        | AlertChange.EvidenceChanged -> "evidence-changed"
        | AlertChange.Resolved -> "resolved"

    type Alert =
        { Id: string
          At: DateTimeOffset
          Minute: int
          Change: AlertChange
          Finding: Finding }

    type Progress =
        { ElapsedMinutes: int
          RemainingMinutes: int
          BuilderCommits: int
          LastCommit: string option
          LastCommitAt: DateTimeOffset option
          PlannedFilesImplemented: int
          PlannedFilesTotal: int
          MandatoryRequirements: int
          FrozenPlanMandatory: int
          CatalogMandatory: int
          CodeCommittedRequirements: int
          DemonstratedRequirements: int
          VerifiedFrozenPlan: int
          VerifiedCatalog: int
          NotEvaluated: int
          TestRunsCompleted: int
          TestRunsPassed: int
          LiveRoundTripWrites: int
          TasksDue: string list
          TasksDueUnaddressed: string list }

    [<RequireQualifiedAccess>]
    type Readiness =
        | Ready
        | Partial
        | Insufficient

    let readinessCode r =
        match r with
        | Readiness.Ready -> "ready"
        | Readiness.Partial -> "partial"
        | Readiness.Insufficient -> "insufficient"

    type RecoveryCheck = { Item: string; Present: bool; Detail: string }

    type Recovery =
        { Readiness: Readiness
          Checks: RecoveryCheck list
          Missing: string list }

    type CheckpointResult =
        { Label: string
          At: DateTimeOffset
          Minute: int
          Mode: Visibility
          Trigger: string
          VisibleCommits: string list
          HeadSha: string option
          Progress: Progress
          Findings: Finding list
          Alerts: Alert list
          Recovery: Recovery
          Limitations: string list }

    type Handoff =
        { GeneratedAt: DateTimeOffset
          T0: DateTimeOffset
          Deadline: DateTimeOffset
          RemainingMinutes: int
          CurrentCommit: string option
          CompletedFunctionality: string list
          VerifiedRequirements: string list
          NotApplicable: string list
          RemainingMandatory: string list
          RemainingByArea: (string * int) list
          Blockers: string list
          NextActions: string list
          PreserveInstructions: string list
          ClockInstructions: string list
          EvidenceBasis: string list }
