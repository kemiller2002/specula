namespace Specula.Observer.Cli

open System
open System.IO
open System.Text.Json
open Specula.Engine
open Specula.Observer.Cli.Common

/// Post-replay evaluation. Runs only after the blind replay has written its
/// records; it is the only command that reads the revealed ground truth.
module Evaluate =

    let private str (name: string) (el: JsonElement) = Json.str name el |> Option.defaultValue ""
    let private strs name el = Json.array name el |> List.choose Json.asString

    let run (argv: string list) =
        let results = requiredArg "--results" argv
        let truth = readJson (requiredArg "--ground-truth" argv)
        let out = requiredArg "--out" argv
        let adjudication =
            arg "--adjudication" argv
            |> Option.map readJson
            |> Option.map (Json.array "alerts" >> List.map (fun a -> str "key" a, a) >> Map.ofList)
            |> Option.defaultValue Map.empty
        let modes = [ "local"; "remote" ]
        let alertsOf mode = Json.array "alerts" (readJson (Path.Combine(results, $"alerts-{mode}.json")))
        let targets = Json.array "targets" truth
        let detection =
            targets
            |> List.map (fun t ->
                let prefixes = strs "observerRules" t
                let perMode =
                    modes
                    |> List.map (fun mode ->
                        let hits =
                            alertsOf mode
                            |> List.filter (fun a -> str "change" a <> "resolved" && prefixes |> List.exists (fun p -> (str "key" a).StartsWith p))
                            |> List.filter (fun a -> Json.path [ "finding"; "kind" ] a |> Option.bind Json.asString = Some "observation")
                            |> List.sortBy (fun a -> Json.int64 "minute" a |> Option.defaultValue 0L)
                        let first = hits |> List.tryHead
                        let firstCritical = hits |> List.tryFind (fun a -> str "severity" a = "critical")
                        mode,
                        jobj
                            [ "detected", jbool first.IsSome
                              "firstAlert", jopt jstr (first |> Option.map (str "id"))
                              "firstMinute", jopt jint64 (first |> Option.bind (Json.int64 "minute"))
                              "firstAt", jopt jstr (first |> Option.map (str "at"))
                              "firstSeverity", jopt jstr (first |> Option.map (str "severity"))
                              "firstTitle", jopt jstr (first |> Option.bind (Json.strAt [ "finding"; "title" ]))
                              "firstCriticalMinute", jopt jint64 (firstCritical |> Option.bind (Json.int64 "minute"))
                              "alerts", jint hits.Length ])
                jobj
                    [ "id", jstr (str "id" t)
                      "question", jstr (str "question" t)
                      "groundTruth", jstr (str "groundTruth" t)
                      "groundTruthSource", jstr (str "source" t)
                      "historicalEvidenceMinute", jopt jint64 (Json.int64 "historicalEvidenceMinute" t)
                      "byMode", perMode |> List.map (fun (m, n) -> jobj [ "mode", jstr m; "result", n ]) |> jarr ])
        let tallies =
            modes
            |> List.map (fun mode ->
                let all = alertsOf mode
                let count pred = all |> List.filter pred |> List.length
                let keys = all |> List.map (str "key") |> List.distinct
                let verdict k = adjudication |> Map.tryFind k |> Option.map (str "verdict") |> Option.defaultValue "not adjudicated"
                jobj
                    [ "mode", jstr mode
                      "alerts", jint all.Length
                      "critical", jint (count (fun a -> str "severity" a = "critical"))
                      "warning", jint (count (fun a -> str "severity" a = "warning"))
                      "informational", jint (count (fun a -> str "severity" a = "informational"))
                      "uncertainty", jint (count (fun a -> Json.path [ "finding"; "kind" ] a |> Option.bind Json.asString = Some "uncertainty"))
                      "distinctFindings", jint keys.Length
                      "adjudicated",
                      keys
                      |> List.map (fun k ->
                          jobj
                              [ "key", jstr k
                                "verdict", jstr (verdict k)
                                "rationale", jstr (adjudication |> Map.tryFind k |> Option.map (str "rationale") |> Option.defaultValue "") ])
                      |> jarr
                      "unsupportedOrFalse", jint (keys |> List.filter (fun k -> verdict k = "unsupported" || verdict k = "false") |> List.length) ])
        writeJson
            (Path.Combine(out, "evaluation.json"))
            (jobj
                [ "schema", jstr "specula.observer-evaluation/1"
                  "groundTruthSha256", jstr (sha256File (requiredArg "--ground-truth" argv))
                  "detection", jarr detection
                  "tallies", jarr tallies ])
