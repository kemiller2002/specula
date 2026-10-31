namespace Specula.Engine

open System
open System.Text.RegularExpressions

/// SPEC-P-001, SPEC-S-003, SPEC-S-006: repository input normalization and
/// GitHub URL validation.
module Repo =

    type InputError =
        | EmptyInput
        | UnsupportedScheme of scheme: string
        | UnsupportedHost of host: string
        | MissingRepository
        | InvalidOwner of owner: string
        | InvalidName of name: string

    let describe (error: InputError) : string =
        match error with
        | EmptyInput -> "Enter a public GitHub repository as owner/repo or a github.com URL."
        | UnsupportedScheme scheme -> $"Only https://github.com URLs are accepted (found \"{scheme}:\")."
        | UnsupportedHost host -> $"Only github.com repositories are supported (found \"{host}\")."
        | MissingRepository -> "Include both the owner and the repository name, for example kemiller2002/specula."
        | InvalidOwner owner -> $"\"{owner}\" is not a valid GitHub owner name."
        | InvalidName name -> $"\"{name}\" is not a valid GitHub repository name."

    let private ownerPattern = Regex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$", RegexOptions.CultureInvariant)
    let private namePattern = Regex("^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)
    let private schemePattern = Regex("^([A-Za-z][A-Za-z0-9+.-]*):", RegexOptions.CultureInvariant)

    let private validate (owner: string) (name: string) : Result<RepoId, InputError> =
        let name = if name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) then name.Substring(0, name.Length - 4) else name
        if not (ownerPattern.IsMatch owner) then Error(InvalidOwner owner)
        elif not (namePattern.IsMatch name) || name = "." || name = ".." then Error(InvalidName name)
        else Ok { Owner = owner; Name = name }

    let private segments (path: string) =
        path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.toList

    let private fromPath (path: string) : Result<RepoId, InputError> =
        let path = path.Split([| '?'; '#' |]).[0]
        match segments path with
        | owner :: name :: _ -> validate owner name
        | _ -> Error MissingRepository

    let private stripHost (rest: string) : Result<string, InputError> =
        let host = rest.Split('/').[0]
        match host.ToLowerInvariant() with
        | "github.com"
        | "www.github.com" -> Ok(rest.Substring(host.Length))
        | other -> Error(UnsupportedHost other)

    /// Accepts owner/repo, github.com/owner/repo and https://github.com/owner/repo[...].
    let parse (input: string) : Result<RepoId, InputError> =
        let text = (Option.ofObj input |> Option.defaultValue "").Trim()
        let schemeMatch = schemePattern.Match text
        if text = "" then
            Error EmptyInput
        elif schemeMatch.Success then
            match schemeMatch.Groups.[1].Value.ToLowerInvariant() with
            | "https" when text.Substring(schemeMatch.Length).StartsWith("//") ->
                text.Substring(schemeMatch.Length + 2) |> stripHost |> Result.bind fromPath
            | scheme -> Error(UnsupportedScheme scheme)
        elif text.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
             || text.StartsWith("www.github.com/", StringComparison.OrdinalIgnoreCase) then
            stripHost text |> Result.bind fromPath
        elif text.Contains '.' && (segments text |> List.tryHead |> Option.exists (fun h -> h.Contains '.')) then
            Error(UnsupportedHost(text.Split('/').[0]))
        else
            match segments text with
            | [ owner; name ] -> validate owner name
            | [ _ ] -> Error MissingRepository
            | _ -> Error MissingRepository

    /// SPEC-S-006: only https://github.com links are rendered as source links.
    let isGitHubUrl (url: string) : bool =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri ->
            uri.Scheme = Uri.UriSchemeHttps
            && uri.Host = "github.com"
            && uri.IsDefaultPort
            && uri.UserInfo = ""
        | _ -> false

    let safeGitHubUrl (url: string) : string option =
        if isGitHubUrl url then Some url else None

    /// External (non-GitHub) https links, e.g. a deployment environment URL.
    let safeHttpsUrl (url: string) : string option =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttps && uri.UserInfo = "" -> Some uri.AbsoluteUri
        | _ -> None

    let apiBase = "https://api.github.com"

    let repoUrl (repo: RepoId) = $"https://github.com/{Uri.EscapeDataString repo.Owner}/{Uri.EscapeDataString repo.Name}"

    let apiRepo (repo: RepoId) = $"{apiBase}/repos/{Uri.EscapeDataString repo.Owner}/{Uri.EscapeDataString repo.Name}"

    let shortSha (sha: string) = if sha.Length > 7 then sha.Substring(0, 7) else sha
