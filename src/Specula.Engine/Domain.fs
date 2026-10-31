namespace Specula.Engine

open System

/// Observed GitHub facts, normalized. Every record keeps the identifiers and
/// source URL needed to verify it on GitHub.
[<AutoOpen>]
module Domain =

    type RepoId = { Owner: string; Name: string }

    module RepoId =
        let text (repo: RepoId) = $"{repo.Owner}/{repo.Name}"
        /// GitHub owner and repository names are case-insensitive.
        let key (repo: RepoId) = (text repo).ToLowerInvariant()
        let same (a: RepoId) (b: RepoId) = key a = key b

    type Repository =
        { Id: RepoId
          DefaultBranch: string
          HtmlUrl: string
          Description: string option
          IsPrivate: bool
          IsArchived: bool
          CreatedAt: DateTimeOffset option
          PushedAt: DateTimeOffset option }

    type Commit =
        { Sha: string
          Message: string
          AuthorName: string option
          AuthorLogin: string option
          Date: DateTimeOffset option
          HtmlUrl: string }

    type PullState =
        | PullOpen
        | PullClosed
        | PullMerged

    type PullRequest =
        { Number: int64
          Title: string
          State: PullState
          Draft: bool
          Author: string option
          CreatedAt: DateTimeOffset option
          UpdatedAt: DateTimeOffset option
          ClosedAt: DateTimeOffset option
          MergedAt: DateTimeOffset option
          HeadSha: string option
          MergeSha: string option
          HtmlUrl: string }

    /// SPEC-UI-004: queued, running, passed, failed, cancelled, skipped, unknown.
    type Outcome =
        | Queued
        | Running
        | Passed
        | Failed
        | Cancelled
        | Skipped
        | Neutral
        | UnknownOutcome

    type WorkflowRun =
        { Id: int64
          Name: string
          HeadSha: string
          HeadBranch: string option
          Event: string option
          Outcome: Outcome
          RunAttempt: int64
          CreatedAt: DateTimeOffset option
          UpdatedAt: DateTimeOffset option
          HtmlUrl: string }

    type CheckRun =
        { Id: int64
          Name: string
          HeadSha: string
          Outcome: Outcome
          App: string option
          StartedAt: DateTimeOffset option
          CompletedAt: DateTimeOffset option
          HtmlUrl: string }

    type DeploymentState =
        | DeploySuccess
        | DeployFailure
        | DeployError
        | DeployInactive
        | DeployInProgress
        | DeployQueued
        | DeployPending
        | DeployUnknown of string

    type DeploymentStatus =
        { Id: int64
          State: DeploymentState
          CreatedAt: DateTimeOffset option
          EnvironmentUrl: string option
          LogUrl: string option }

    type Deployment =
        { Id: int64
          Sha: string
          Ref: string option
          Environment: string
          Creator: string option
          CreatedAt: DateTimeOffset option
          StatusesUrl: string option }

    type EventKind =
        | CommitEvent
        | PullRequestEvent
        | WorkflowEvent
        | CheckEvent
        | DeploymentEvent

    /// One normalized, source-linked observation (CAPABILITIES SourceEvent).
    type SourceEvent =
        { StableId: string
          Kind: EventKind
          Title: string
          Summary: string
          SourceUrl: string
          EventTime: DateTimeOffset
          Sha: string option
          Actor: string option
          Outcome: Outcome option }

    type EvidenceState =
        | Verified
        | FailedEvidence
        | Blocked
        | Unverified
        | Unavailable of reason: string
