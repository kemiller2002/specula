namespace Specula.Engine

open System
open Limen.Contract.Core
open Limen.Guest
open Specula.Engine.Model
open Specula.Engine.Update

/// The single impure seam: the Limen transport calls `handle` with one
/// serialized browser message and receives one serialized response. The
/// current model lives in one reference cell; every decision is made by the
/// pure `Update.update` and `View.project` functions. The device clock and
/// time zone are read here and passed in as data.
module Dispatch =

    let private localZone () =
        try TimeZoneInfo.Local with _ -> TimeZoneInfo.Utc

    let private state = ref (Model.initial DateTimeOffset.UtcNow (localZone ()))

    let private toMsg (message: BrowserToEngineMessage) : Msg option =
        match message with
        | BrowserToEngineMessage.Initialize(_, location, _) -> Some(Init location)
        | BrowserToEngineMessage.Event event -> Some(Update.ofEvent event)
        | BrowserToEngineMessage.LocationChanged location -> Some(LocationChanged location)
        | BrowserToEngineMessage.EffectResult(EffectResult.HttpResult(CorrelationId id, outcome)) -> Some(HttpDone(id, outcome))
        | BrowserToEngineMessage.EffectResult(EffectResult.StorageResult(CorrelationId id, outcome)) -> Some(StorageDone(id, outcome))
        | BrowserToEngineMessage.EffectResult(EffectResult.NavigationResult _) -> Some Ignored
        | BrowserToEngineMessage.EffectResult(EffectResult.ClipboardResult _)
        | BrowserToEngineMessage.EffectResult(EffectResult.CapabilityResult _) -> Some Ignored
        | BrowserToEngineMessage.CapabilityFact _ -> Some Ignored

    /// Pure core of the boundary, used directly by tests.
    let step (now: DateTimeOffset) (message: BrowserToEngineMessage) (model: Model) : Model * EngineToBrowserMessage =
        let model = { model with Now = now }
        let handshake =
            match message with
            | BrowserToEngineMessage.Initialize(_, _, offer) -> Some(Handshake.answer offer Handshake.coreOnly)
            | _ -> None
        let result =
            match toMsg message with
            | Some msg -> Update.update msg model
            | None -> { Model = model; Effects = []; Cancellations = [] }
        result.Model,
        { View = View.project result.Model
          Effects = result.Effects
          Cancellations = result.Cancellations
          Handshake = handshake }

    let handle (messageJson: string) : string =
        let message =
            match Codec.parseBrowserToEngineMessage messageJson with
            | Ok message -> message
            | Error error -> failwith $"Browser message outside the Limen contract at {error.Path}: expected {error.Expected}, found {error.Found}"
        let next, response = step DateTimeOffset.UtcNow message state.Value
        state.Value <- next
        Codec.serializeEngineToBrowserMessage response

    let resetForTests (zone: TimeZoneInfo) =
        state.Value <- Model.initial DateTimeOffset.UtcNow zone
