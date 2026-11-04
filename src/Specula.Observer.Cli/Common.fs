namespace Specula.Observer.Cli

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Specula.Engine.Observer

/// Shared helpers: deterministic JSON construction, hashing and the only
/// git process runner, which admits read commands alone.
module Common =

    // ---- JSON construction (ordered, deterministic) ----

    let jstr (s: string) : JsonNode = JsonValue.Create(s)
    let jint (n: int) : JsonNode = JsonValue.Create(n)
    let jint64 (n: int64) : JsonNode = JsonValue.Create(n)
    let jbool (b: bool) : JsonNode = JsonValue.Create(b)
    let jnull : JsonNode = null
    let jopt (f: 'a -> JsonNode) (v: 'a option) : JsonNode = v |> Option.map f |> Option.defaultValue jnull
    let jtime (t: DateTimeOffset) : JsonNode = jstr (t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))

    let jarr (items: JsonNode seq) : JsonNode =
        let a = JsonArray()
        items |> Seq.iter (fun i -> a.Add i)
        a :> JsonNode

    let jstrs (items: string seq) = items |> Seq.map jstr |> jarr

    let jobj (fields: (string * JsonNode) list) : JsonNode =
        let o = JsonObject()
        fields |> List.iter (fun (k, v) -> o.Add(k, v))
        o :> JsonNode

    let private writeOptions = JsonSerializerOptions(WriteIndented = true, Encoder = Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    let render (node: JsonNode) = node.ToJsonString(writeOptions) + "\n"

    let writeJson (path: string) (node: JsonNode) =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, render node)

    let writeText (path: string) (text: string) =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, text)

    let sha256Bytes (bytes: byte[]) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let sha256Text (text: string) = sha256Bytes (Encoding.UTF8.GetBytes text)
    let sha256File (path: string) = sha256Bytes (File.ReadAllBytes path)

    let readJson (path: string) = JsonDocument.Parse(File.ReadAllText path).RootElement.Clone()

    // ---- read-only git ----

    /// Counts every git invocation so the compute report is measured.
    let mutable gitInvocations = 0

    /// Run git in `repo` after ReadOnly.admit accepts the arguments. A refused
    /// command never starts a process.
    let git (repo: string) (args: string list) : Result<string, string> =
        match ReadOnly.admit args with
        | Error refusal -> Error $"refused: {refusal.Reason}"
        | Ok admitted ->
            gitInvocations <- gitInvocations + 1
            let info = ProcessStartInfo("git", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)
            info.ArgumentList.Add "-C"
            info.ArgumentList.Add repo
            // Lazy fetches of a partial clone would write to the observer's
            // copy and contact the network; forbid them.
            info.Environment["GIT_NO_LAZY_FETCH"] <- "1"
            info.Environment["GIT_TERMINAL_PROMPT"] <- "0"
            admitted |> List.iter info.ArgumentList.Add
            use p = Process.Start info
            let out = p.StandardOutput.ReadToEnd()
            let err = p.StandardError.ReadToEnd()
            p.WaitForExit()
            if p.ExitCode = 0 then Ok out else Error(err.Trim())

    let gitOr (repo: string) (args: string list) (fallback: string) =
        match git repo args with
        | Ok s -> s
        | Error _ -> fallback

    let lines (s: string) = s.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.toList

    let arg (name: string) (argv: string list) =
        argv |> List.pairwise |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

    let argOr name fallback argv = arg name argv |> Option.defaultValue fallback

    let requiredArg name argv =
        match arg name argv with
        | Some v -> v
        | None -> failwith $"missing {name}"
