﻿namespace Koffee

open System
open FSharp.Desktop.UI
open ConfigExt

type SettingsController(config: Config) =
    interface IController<SettingsEvents, SettingsModel> with
        member this.InitModel model =
            model.KeyBindings <-
                MainEvents.Bindable
                |> List.map (fun evt ->
                    let keys =
                        KeyBinding.DefaultsAsString
                        |> List.choose (fun (key, bound) ->
                            if bound = evt then Some key
                            else None)
                    {
                        EventName = evt.FriendlyName
                        BoundKeys = String.Join(" OR ", keys)
                    })

        member x.Dispatcher = function
            | StartupPathChanged value -> Sync (fun _ ->
                config.StartupPath <- value; config.Save())
            | DefaultPathChanged value -> Sync (fun _ ->
                config.DefaultPath <- value; config.Save())
            | TextEditorChanged value -> Sync (fun _ ->
                config.TextEditor <- value; config.Save())
            | PathFormatChanged value -> Sync (fun _ ->
                config.PathFormat <- value; config.Save())
            | ShowFullPathInTitleChanged value -> Sync (fun _ ->
                config.Window.ShowFullPathInTitle <- value; config.Save())
            | ShowHiddenChanged value -> Sync (fun _ ->
                config.ShowHidden <- value; config.Save())
