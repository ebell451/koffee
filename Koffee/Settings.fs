﻿module Koffee.Settings

open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls.Primitives
open VinylUI
open VinylUI.Wpf

type KeyBind = {
    EventName: string
    BoundKeys: string
}

type Model = {
    KeyBindings: KeyBind list
}

type Events =
    | StartPathChanged of StartPath
    | DefaultPathChanged of string
    | TextEditorChanged of string
    | CommandlinePathChanged of string
    | PathFormatChanged of PathFormat
    | ShowFullPathInTitleChanged of bool
    | ShowHiddenChanged of bool
    | SearchCaseSensitiveChanged of bool
    | RefreshWindowOnActivate of bool

type View = FsXaml.XAML<"SettingsWindow.xaml">

let private binder (config: Config) (view: View) model =
    let check (ctl: ToggleButton) =
        ctl.IsChecked <- Nullable true

    check (if config.StartPath = RestorePrevious then view.StartPathPrevious else view.StartPathDefault)
    check (if config.PathFormat = Windows then view.PathFormatWindows else view.PathFormatUnix)
    view.DefaultPath.Text <- config.DefaultPath

    view.TextEditor.Text <- config.TextEditor
    view.CommandlinePath.Text <- config.CommandlinePath

    if config.Window.ShowFullPathInTitle then
        check view.ShowFullPathInTitleBar
    if config.ShowHidden then
        check view.ShowHidden
    if config.SearchCaseSensitive then
        check view.SearchCaseSensitive
    if config.Window.RefreshOnActivate then
        check view.RefreshOnActivate

    view.KeyBindings.AddColumn("EventName", "Command", widthWeight = 3.0)
    view.KeyBindings.AddColumn("BoundKeys", "Bound Keys")

    view.PreviewKeyDown.Add (onKey Key.Escape view.Close)
    view.PreviewKeyDown.Add (onKeyCombo ModifierKeys.Control Key.W view.Close)

    [ Bind.model(<@ model.KeyBindings @>).toItemsSource(view.KeyBindings, <@ fun kb -> kb.BoundKeys, kb.EventName @>)
    ]

let private events (view: View) =
    let textBoxChanged evt (textBox: Controls.TextBox) =
        textBox.LostFocus |> Observable.map (fun _ -> evt (textBox.Text))

    let checkedChanged evt (checkBox: Controls.CheckBox) =
        (checkBox.Checked |> Observable.mapTo (evt true),
         checkBox.Unchecked |> Observable.mapTo (evt false))
        ||> Observable.merge

    [ view.StartPathPrevious.Checked |> Observable.mapTo (StartPathChanged RestorePrevious)
      view.StartPathDefault.Checked |> Observable.mapTo (StartPathChanged DefaultPath)
      view.DefaultPath |> textBoxChanged DefaultPathChanged

      view.TextEditor |> textBoxChanged TextEditorChanged
      view.CommandlinePath |> textBoxChanged CommandlinePathChanged

      view.PathFormatWindows.Checked |> Observable.mapTo (PathFormatChanged Windows)
      view.PathFormatUnix.Checked |> Observable.mapTo (PathFormatChanged Unix)

      view.ShowFullPathInTitleBar |> checkedChanged ShowFullPathInTitleChanged
      view.ShowHidden |> checkedChanged ShowHiddenChanged
      view.SearchCaseSensitive |> checkedChanged SearchCaseSensitiveChanged
      view.RefreshOnActivate |> checkedChanged RefreshWindowOnActivate
    ]

let private dispatcher (config: Config) evt =
    let configHandler f = Sync (fun m -> f (); config.Save(); m)
    match evt with
    | StartPathChanged value -> configHandler (fun _ ->
        config.StartPath <- value)
    | DefaultPathChanged value -> configHandler (fun _ ->
        config.DefaultPath <- value)
    | TextEditorChanged value -> configHandler (fun _ ->
        config.TextEditor <- value)
    | CommandlinePathChanged value -> configHandler (fun _ ->
        config.CommandlinePath <- value)
    | PathFormatChanged value -> configHandler (fun _ ->
        config.PathFormat <- value)
    | ShowFullPathInTitleChanged value -> configHandler (fun _ ->
        config.Window.ShowFullPathInTitle <- value)
    | ShowHiddenChanged value -> configHandler (fun _ ->
        config.ShowHidden <- value)
    | SearchCaseSensitiveChanged value -> configHandler (fun _ ->
        config.SearchCaseSensitive <- value)
    | RefreshWindowOnActivate value -> configHandler (fun _ ->
        config.Window.RefreshOnActivate <- value)

let start config view =
    let keyBinding (evt: MainEvents) = {
        EventName = evt.FriendlyName
        BoundKeys =
            KeyBinding.defaults
            |> List.filter (snd >> ((=) evt))
            |> List.map (fst >> Seq.map KeyBinding.keyDescription >> String.concat "")
            |> String.concat " OR "
    }
    let model = { KeyBindings = MainEvents.Bindable |> List.map keyBinding }
    Framework.start (binder config) events (dispatcher config) view model
