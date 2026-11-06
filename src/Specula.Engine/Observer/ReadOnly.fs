namespace Specula.Engine.Observer

open System

/// The observer's only way to touch a monitored repository.
///
/// A command is admitted only when its subcommand is a read and none of its
/// arguments can write (an output file, a config change, an execution hook).
/// Anything else is refused before a process starts. The collector routes
/// every git invocation through `admit`; HTTP access is GET-only.
module ReadOnly =

    /// Subcommands that only read objects and refs.
    let readSubcommands =
        set [ "log"; "show"; "ls-tree"; "rev-parse"; "rev-list"; "cat-file"; "diff-tree"; "merge-base"; "for-each-ref"; "name-rev" ]

    /// Argument prefixes that would make a read command write or execute.
    let forbiddenArguments =
        [ "--output"; "-o"; "--ext-diff"; "--textconv"; "--exec"; "-c"; "--config-env"; "--git-dir="; "--work-tree=" ]

    /// Subcommands a builder uses to change history or the remote. Listed so
    /// a refusal names the reason; anything not in readSubcommands is
    /// refused even if it is not listed here.
    let writeSubcommands =
        set [ "commit"; "push"; "merge"; "rebase"; "reset"; "checkout"; "switch"; "restore"; "add"; "rm"; "mv"; "tag"; "branch"
              "stash"; "clean"; "am"; "apply"; "cherry-pick"; "revert"; "fetch"; "pull"; "gc"; "prune"; "update-ref"; "config"
              "notes"; "worktree"; "init"; "clone"; "submodule"; "filter-branch"; "replace"; "symbolic-ref"; "reflog" ]

    type Refusal = { Command: string list; Reason: string }

    let private isForbiddenArgument (arg: string) =
        forbiddenArguments
        |> List.exists (fun prefix ->
            arg = prefix || (prefix.StartsWith("--") && arg.StartsWith(prefix + "=")) || (prefix.EndsWith("=") && arg.StartsWith(prefix)))

    /// Admit a git argument vector (without the leading "git") or refuse it.
    let admit (args: string list) : Result<string list, Refusal> =
        match args with
        | [] -> Error { Command = args; Reason = "empty command" }
        | sub :: rest ->
            if writeSubcommands.Contains sub then
                Error { Command = args; Reason = $"'{sub}' changes the repository; the observer is read-only" }
            elif not (readSubcommands.Contains sub) then
                Error { Command = args; Reason = $"'{sub}' is not an allowlisted read" }
            else
                match rest |> List.tryFind isForbiddenArgument with
                | Some arg -> Error { Command = args; Reason = $"argument '{arg}' could write or execute" }
                | None -> Ok args

    /// HTTP methods the observer may send. Anything else is refused.
    let admitHttpMethod (httpMethod: string) : Result<string, string> =
        match httpMethod.Trim().ToUpperInvariant() with
        | "GET" | "HEAD" -> Ok(httpMethod.Trim().ToUpperInvariant())
        | other -> Error $"HTTP {other} could change the monitored repository; the observer only reads"
