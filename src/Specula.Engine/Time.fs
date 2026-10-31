namespace Specula.Engine

open System
open System.Globalization

/// Viewer-local presentation of timestamps (EXPERIENCE §2, SPEC-V-009).
/// All stored instants are UTC DateTimeOffsets; conversion happens only at
/// projection time with an explicit zone.
module Time =

    let private inv = CultureInfo.InvariantCulture

    let toLocal (zone: TimeZoneInfo) (instant: DateTimeOffset) : DateTimeOffset =
        TimeZoneInfo.ConvertTime(instant, zone)

    let offsetLabel (offset: TimeSpan) : string =
        if offset = TimeSpan.Zero then "UTC"
        else
            let sign = if offset < TimeSpan.Zero then "−" else "+"
            let abs = offset.Duration()
            $"UTC{sign}{abs.Hours:D2}:{abs.Minutes:D2}"

    /// "2026-10-31 09:41 UTC−04:00"
    let local (zone: TimeZoneInfo) (instant: DateTimeOffset) : string =
        let l = toLocal zone instant
        l.ToString("yyyy-MM-dd HH:mm", inv) + " " + offsetLabel l.Offset

    let localShort (zone: TimeZoneInfo) (instant: DateTimeOffset) : string =
        let l = toLocal zone instant
        l.ToString("MMM d, HH:mm", inv)

    let utc (instant: DateTimeOffset) : string =
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", inv)

    let iso (instant: DateTimeOffset) : string = utc instant

    let duration (span: TimeSpan) : string =
        let span = span.Duration()
        if span.TotalMinutes < 1.0 then $"{int span.TotalSeconds}s"
        elif span.TotalHours < 1.0 then $"{int span.TotalMinutes}m"
        elif span.TotalDays < 1.0 then $"{int span.TotalHours}h {span.Minutes}m"
        else $"{int span.TotalDays}d {span.Hours}h"

    let ago (now: DateTimeOffset) (instant: DateTimeOffset) : string =
        let span = now - instant
        if span < TimeSpan.Zero then "in the future (check device clock)"
        elif span.TotalSeconds < 45.0 then "just now"
        else duration span + " ago"

    /// Parses an <input type="datetime-local"> value ("yyyy-MM-ddTHH:mm[:ss]")
    /// as wall-clock time in the given zone.
    let parseLocalInput (zone: TimeZoneInfo) (text: string) : DateTimeOffset option =
        let formats = [| "yyyy-MM-dd'T'HH:mm"; "yyyy-MM-dd'T'HH:mm:ss"; "yyyy-MM-dd'T'HH:mm:ss.fff" |]
        match DateTime.TryParseExact((Option.ofObj text |> Option.defaultValue "").Trim(), formats, inv, DateTimeStyles.None) with
        | true, wall ->
            let unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified)
            if zone.IsInvalidTime unspecified then None
            else Some(DateTimeOffset(unspecified, zone.GetUtcOffset unspecified).ToUniversalTime())
        | _ -> None

    /// Formats an instant for an <input type="datetime-local"> value.
    let toLocalInput (zone: TimeZoneInfo) (instant: DateTimeOffset) : string =
        (toLocal zone instant).ToString("yyyy-MM-dd'T'HH:mm", inv)

    let parseIso (text: string) : DateTimeOffset option = Json.parseTime text

    let median (values: TimeSpan list) : TimeSpan option =
        match values |> List.sort with
        | [] -> None
        | sorted ->
            let n = List.length sorted
            if n % 2 = 1 then Some sorted.[n / 2]
            else Some(TimeSpan.FromTicks((sorted.[n / 2 - 1].Ticks + sorted.[n / 2].Ticks) / 2L))
