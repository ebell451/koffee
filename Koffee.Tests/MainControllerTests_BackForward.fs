﻿module Koffee.MainControllerTests_BackForward

open System.Windows.Input
open FSharp.Desktop.UI
open NUnit.Framework
open FsUnitTyped
open Foq
open Testing

let createController () =
    let fileSys =
        Mock<IFileSystemService>()
            .Setup(fun x -> <@ x.GetNodes(any()) @>).Returns([])
            .Create()
    let settingsFactory () = Mock.Of<Mvc<SettingsEvents, SettingsModel>>()
    MainController(fileSys, settingsFactory)

let history contrFunc backStack forwardStack =
    let model = createBaseTestModel()
    model.BackStack <- backStack |> List.map (fun (p, c) -> (createPath p, c))
    model.ForwardStack <- forwardStack |> List.map (fun (p, c) -> (createPath p, c))
    model.Path <- createPath "path"
    model.Cursor <- 1
    model.Status <- "previous status"
    let contr = createController()
    contrFunc contr model
    model

let back = history (fun contr -> contr.Back)
let forward = history (fun contr -> contr.Forward)

[<Test>]
let ``Back without history does nothing``() =
    let model = back [] []
    model.Path |> shouldEqual (createPath "path")
    model.Cursor |> shouldEqual 1
    model.BackStack |> shouldEqual []
    model.ForwardStack |> shouldEqual []
    model.Status |> shouldEqual "previous status"

[<Test>]
let ``Back with simple history changes path and stacks``() =
    let model = back ["back", 2] []
    model.Path |> shouldEqual (createPath "back")
    model.Cursor |> shouldEqual 2
    model.BackStack |> shouldEqual []
    model.ForwardStack |> shouldEqual [createPath "path", 1]
    model.Status |> shouldEqual ""

[<Test>]
let ``Back with more history changes path and stacks``() =
    let model = back ["back1", 2; "back2", 3] ["fwd1", 4; "fwd2", 5]
    model.Path |> shouldEqual (createPath "back1")
    model.Cursor |> shouldEqual 2
    model.BackStack |> shouldEqual [createPath "back2", 3]
    model.ForwardStack |> shouldEqual [createPath "path", 1; createPath "fwd1", 4; createPath "fwd2", 5]
    model.Status |> shouldEqual ""

[<Test>]
let ``Forward without history does nothing``() =
    let model = forward [] []
    model.Path |> shouldEqual (createPath "path")
    model.Cursor |> shouldEqual 1
    model.BackStack |> shouldEqual []
    model.ForwardStack |> shouldEqual []
    model.Status |> shouldEqual "previous status"

[<Test>]
let ``Forward with simple history changes path and stacks``() =
    let model = forward [] ["fwd", 2]
    model.Path |> shouldEqual (createPath "fwd")
    model.Cursor |> shouldEqual 2
    model.BackStack |> shouldEqual [createPath "path", 1]
    model.ForwardStack |> shouldEqual []
    model.Status |> shouldEqual ""

[<Test>]
let ``Forward with more history changes path and stacks``() =
    let model = forward ["back1", 2; "back2", 3] ["fwd1", 4; "fwd2", 5]
    model.Path |> shouldEqual (createPath "fwd1")
    model.Cursor |> shouldEqual 4
    model.BackStack |> shouldEqual [createPath "path", 1; createPath "back1", 2; createPath "back2", 3]
    model.ForwardStack |> shouldEqual [createPath "fwd2", 5]
    model.Status |> shouldEqual ""
