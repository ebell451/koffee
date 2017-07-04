﻿module Koffee.Program

open System.Windows
open FSharp.Desktop.UI

let makeSettingsMvc () =
    let window = SettingsWindow()
    let model = SettingsModel.Create()
    let view = SettingsView(window)
    let controller = SettingsController()
    Mvc(model, view, controller)

[<EntryPoint>]
[<System.STAThread>]
let main args =
    let config = Config()
    let model = MainModel.Create()
    let window = MainWindow()
    let view = MainView(window, KeyBinding.Defaults, config)
    let fileSys = FileSystemService()
    let controller = MainController(fileSys, makeSettingsMvc, config)
    let mvc = Mvc(model, view, controller)

    if args.Length > 0 then
        match Path.Parse args.[0] with
        | Some p -> model.Path <- p
        | None -> model.Status <- Some <| MainStatus.invalidPath args.[0]

    use eventLoop = mvc.Start()

    let app = Application()
    app.Run window |> ignore
    0
