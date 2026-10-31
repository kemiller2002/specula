namespace Specula.Engine

open System
open System.Globalization
open System.Text.Json

/// Total, option-returning readers over untrusted JSON. GitHub responses and
/// evidence files are data, never instructions: every accessor tolerates a
/// missing or mistyped field instead of throwing.
module Json =

    let tryParse (text: string) : JsonElement option =
        try
            use document = JsonDocument.Parse(text)
            Some(document.RootElement.Clone())
        with _ ->
            None

    let prop (name: string) (element: JsonElement) : JsonElement option =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty name with
            | true, value when value.ValueKind <> JsonValueKind.Null && value.ValueKind <> JsonValueKind.Undefined -> Some value
            | _ -> None
        | _ -> None

    let path (names: string list) (element: JsonElement) : JsonElement option =
        names |> List.fold (fun acc name -> acc |> Option.bind (prop name)) (Some element)

    let asString (element: JsonElement) : string option =
        match element.ValueKind with
        | JsonValueKind.String -> Option.ofObj (element.GetString())
        | _ -> None

    let asInt64 (element: JsonElement) : int64 option =
        match element.ValueKind with
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, value -> Some value
            | _ -> None
        | _ -> None

    let asBool (element: JsonElement) : bool option =
        match element.ValueKind with
        | JsonValueKind.True -> Some true
        | JsonValueKind.False -> Some false
        | _ -> None

    let asArray (element: JsonElement) : JsonElement list =
        match element.ValueKind with
        | JsonValueKind.Array -> element.EnumerateArray() |> Seq.toList
        | _ -> []

    let parseTime (text: string) : DateTimeOffset option =
        match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
        | true, value -> Some value
        | _ -> None

    let str name element = prop name element |> Option.bind asString
    let int64 name element = prop name element |> Option.bind asInt64
    let bool name element = prop name element |> Option.bind asBool
    let time name element = str name element |> Option.bind parseTime
    let array name element = prop name element |> Option.map asArray |> Option.defaultValue []
    let strAt names element = path names element |> Option.bind asString
    let timeAt names element = strAt names element |> Option.bind parseTime

    /// Minimal JSON string escaping for values the engine writes (preferences).
    let quote (value: string) : string =
        let encoded = JsonEncodedText.Encode(value)
        "\"" + encoded.Value + "\""
