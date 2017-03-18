﻿namespace Koffee

open System.ComponentModel
open System.Windows
open System.Windows.Data
open System.Windows.Input
open System.Windows.Controls
open System.Windows.Media
open FSharp.Desktop.UI
open ModelExtensions
open UIHelpers
open Utility

type MainWindow = FsXaml.XAML<"MainWindow.xaml">

type MainView(window: MainWindow, keyBindings: (KeyCombo * MainEvents) list) =
    inherit View<MainEvents, MainModel, MainWindow>(window)

    let mutable currBindings = keyBindings

    let onKeyFunc key resultFunc (keyEvent : IEvent<KeyEventHandler, KeyEventArgs>) =
        keyEvent |> Observable.choose (fun evt ->
            if evt.Key = key then
                evt.Handled <- true
                Some <| resultFunc()
            else
                None)

    override this.SetBindings (model: MainModel) =
        // setup grid
        window.NodeGrid.AddColumn("Name", widthWeight = 3.0)
        window.NodeGrid.AddColumn("Type", converter = ValueConverters.UnionText())
        window.NodeGrid.AddColumn("Modified", converter = ValueConverters.OptionValue(), format = "yyyy-MM-dd  HH:mm")
        window.NodeGrid.AddColumn("SizeFormatted", "Size", alignRight = true)

        // simple bindings
        Binding.OfExpression
            <@
                window.NodeGrid.ItemsSource <- model.Nodes
                window.NodeGrid.SelectedIndex <- model.Cursor
                window.CommandBox.Text <- model.CommandText |> BindingOptions.UpdateSourceOnChange
            @>

        // display path
        let displayPath _ =
            window.PathBox.Text <- model.PathFormatted
            window.Title <-
                model.Path.Name
                |> Str.ifEmpty model.PathFormatted
                |> sprintf "%s  |  Koffee"
        displayPath()
        model.OnPropertyChanged <@ model.Path @> displayPath
        model.OnPropertyChanged <@ model.PathFormat @> displayPath

        // display item in buffer
        window.BufferLabel.Content <- ""
        model.OnPropertyChanged <@ model.ItemBuffer @> (fun buffer ->
            let text = MainView.BufferStatus buffer
            window.BufferLabel.Content <- text
            window.BufferLabel.Visibility <- if text = "" then Visibility.Hidden else Visibility.Visible)

        // update command text selection
        model.OnPropertyChanged <@ model.CommandTextSelection @> (fun (start, len) ->
            window.CommandBox.Select(start, len))

        // update UI for the command input mode
        model.OnPropertyChanged <@ model.CommandInputMode @> (fun mode ->
            this.CommandInputModeChanged mode model.SelectedNode
            if mode.IsNone then model.CommandTextSelection <- (999, 0))

        // update UI for status
        this.UpdateStatus model.Status
        model.OnPropertyChanged <@ model.Status @> this.UpdateStatus

        // bind Tab key to switch focus
        window.PathBox.PreviewKeyDown.Add <| onKey Key.Tab window.NodeGrid.Focus
        window.NodeGrid.PreviewKeyDown.Add <| onKey Key.Tab (fun () ->
            window.PathBox.SelectAll()
            window.PathBox.Focus())

        // on selection change, keep selected node in view, make sure node list is focused
        window.NodeGrid.SelectionChanged.Add (fun _ ->
            this.KeepSelectedInView()
            if not window.NodeGrid.IsFocused then
                window.NodeGrid.Focus() |> ignore)

        // on resize, keep selected node in view, update the page size
        window.NodeGrid.SizeChanged.Add (fun _ ->
            this.KeepSelectedInView()
            match this.ItemsPerPage with
                | Some i -> model.PageSize <- i
                | None -> ())

        // escape and lost focus resets the input mode
        window.PreviewKeyDown.Add <| onKey Key.Escape (fun () ->
            model.Status <- None
            model.CommandInputMode <- None)
        window.CommandBox.LostFocus.Add (fun _ -> model.CommandInputMode <- None)

        // make sure selected item gets set to the cursor
        let desiredCursor = model.Cursor
        model.Cursor <- -1
        window.Loaded.Add (fun _ -> model.Cursor <- desiredCursor)

    override this.EventStreams = [
        window.PathBox.PreviewKeyDown |> onKeyFunc Key.Enter (fun () -> OpenPath window.PathBox.Text)

        window.NodeGrid.PreviewKeyDown |> Observable.choose this.TriggerKeyBindings

        window.CommandBox.PreviewKeyDown |> onKeyFunc Key.Enter (fun () -> ExecuteCommand)
        window.CommandBox.PreviewTextInput |> Observable.choose this.CommandTextInput
    ]

    member this.CommandTextInput keyEvt =
        match keyEvt.Text.ToCharArray() with
        | [| c |] -> Some (CommandCharTyped c)
        | _ -> None

    member this.TriggerKeyBindings evt =
        let modifierKeys = [
            Key.LeftShift; Key.RightShift; Key.LeftCtrl; Key.RightCtrl;
            Key.LeftAlt; Key.RightAlt; Key.LWin; Key.RWin; Key.System
        ]
        let chord = (Keyboard.Modifiers, evt.Key)

        if Seq.contains evt.Key modifierKeys then
            None
        else if chord = (ModifierKeys.None, Key.Escape) then
            evt.Handled <- true
            currBindings <- keyBindings
            None
        else
            match KeyBinding.GetMatch currBindings chord with
            // completed key combo
            | [ ([], Exit) ] ->
                window.Close()
                None
            | [ ([], newEvent) ] ->
                evt.Handled <- true
                currBindings <- keyBindings
                Some newEvent
            // no match
            | [] ->
                currBindings <- keyBindings
                None
            // partial match to one or more key combos
            | matchedBindings ->
                evt.Handled <- true
                currBindings <- matchedBindings
                None

    member this.UpdateStatus status =
        window.StatusLabel.Content <- 
            match status with
            | Some (Message msg) | Some (ErrorMessage msg) | Some (Busy msg) -> msg
            | None -> ""
        window.StatusLabel.Foreground <-
            match status with
            | Some (ErrorMessage _) -> Brushes.Red
            | _ -> SystemColors.WindowTextBrush

        let isBusy =
            match status with
            | Some (Busy _) -> true
            | _ -> false
        let wasBusy = not window.NodeGrid.IsEnabled
        window.PathBox.IsEnabled <- not isBusy
        window.NodeGrid.IsEnabled <- not isBusy
        window.Cursor <- if isBusy then Cursors.Wait else Cursors.Arrow
        if wasBusy && not isBusy then
            window.NodeGrid.Focus() |> ignore

    member this.CommandInputModeChanged mode node =
        match mode with
        | Some inputMode -> this.ShowCommandBar (inputMode.Prompt node)
        | None -> this.HideCommandBar ()

    member private this.ShowCommandBar label =
        window.CommandLabel.Content <- label
        window.CommandPanel.Visibility <- Visibility.Visible
        window.CommandBox.Focus() |> ignore

    member private this.HideCommandBar () =
        window.CommandPanel.Visibility <- Visibility.Hidden
        window.NodeGrid.Focus() |> ignore

    member this.KeepSelectedInView () =
        if window.NodeGrid.SelectedItem <> null then
            window.NodeGrid.ScrollIntoView(window.NodeGrid.SelectedItem)

    member this.ItemsPerPage =
        if window.NodeGrid.HasItems then
            let index = window.NodeGrid.SelectedIndex |> max 0
            let row = window.NodeGrid.ItemContainerGenerator.ContainerFromIndex(index) :?> DataGridRow
            window.NodeGrid.ActualHeight / row.ActualHeight |> int |> Some
        else
            None

    static member BufferStatus buffer =
        match buffer with
        | Some (node, action) -> sprintf "%A %A: %s" action node.Type node.Name
        | None -> ""

module MainStatus =
    // navigation
    let find char = Message <| sprintf "Find %O" char
    let search searchStr = Message <| sprintf "Search \"%s\"" searchStr

    // actions
    let openFile path = Message <| sprintf "Opened File: %s" path
    let openExplorer path = Message <| sprintf "Opened Windows Explorer to: %s" path
    let invalidPath path = ErrorMessage <| sprintf "Path format is invalid: %s" path
    let changePathFormat newFormat = Message <| sprintf "Changed Path Format to %O" newFormat

    let private runningActionMessage action pathFormat =
        match action with
        | MovedItem (node, newPath) -> Some <| sprintf "Moving %s to \"%s\"..." node.Description (newPath.Format pathFormat)
        | CopiedItem (node, newPath) -> Some <| sprintf "Copying %s to \"%s\"..." node.Description (newPath.Format pathFormat)
        | DeletedItem (node, false) -> Some <| sprintf "Recycling %s..." node.Description
        | DeletedItem (node, true) -> Some <| sprintf "Deleting %s..." node.Description
        | _ -> None
    let checkingIsRecyclable = Message <| "Calculating size..."
    let runningAction action pathFormat =
        runningActionMessage action pathFormat |> Option.map (fun m -> Busy m)
    let private actionCompleteMessage action pathFormat =
        match action with
        | CreatedItem node -> sprintf "Created %s" node.Description
        | RenamedItem (node, newName) -> sprintf "Renamed %s to \"%s\"" node.Description newName
        | MovedItem (node, newPath) -> sprintf "Moved %s to \"%s\"" node.Description (newPath.Format pathFormat)
        | CopiedItem (node, newPath) -> sprintf "Copied %s to \"%s\"" node.Description (newPath.Format pathFormat)
        | DeletedItem (node, false) -> sprintf "Sent %s to Recycle Bin" node.Description
        | DeletedItem (node, true) -> sprintf "Deleted %s" node.Description
    let actionComplete action pathFormat =
        actionCompleteMessage action pathFormat |> Message

    let cannotRecycle (node: Node) =
        ErrorMessage <| sprintf "Cannot move %s to the recycle bin because it is too large" node.Description
    let cannotMoveToSameFolder = ErrorMessage <| "Cannot move item to same folder it is already in"
    let cancelled = Message <| "Cancelled"

    let setActionExceptionStatus action ex (model: MainModel) =
        let actionName =
            match action with
            | CreatedItem node -> sprintf "create %s" node.Description
            | RenamedItem (node, newName) -> sprintf "rename %s" node.Description
            | MovedItem (node, newPath) -> sprintf "move %s to \"%s\"" node.Description (newPath.Format model.PathFormat)
            | CopiedItem (node, newPath) -> sprintf "copy %s to \"%s\"" node.Description (newPath.Format model.PathFormat)
            | DeletedItem (node, false) -> sprintf "recycle %s" node.Description
            | DeletedItem (node, true) -> sprintf "delete %s" node.Description
        model.Status <- Some <| StatusType.fromExn actionName ex

    // undo/redo
    let undoingCreate (node: Node) = Busy <| sprintf "Undoing creation of %s - Deleting..." node.Description
    let undoingMove (node: Node) = Busy <| sprintf "Undoing move of %s..." node.Description
    let undoingCopy (node: Node) isDeletionPermanent =
        let undoVerb = if isDeletionPermanent then "Deleting" else "Recycling"
        Busy <| sprintf "Undoing copy of %s - %s..." node.Description undoVerb
    let undoAction action pathFormat =
        Message <| (actionCompleteMessage action pathFormat |> sprintf "Action undone: %s")

    let redoingAction action pathFormat =
        runningActionMessage action pathFormat
            |> Option.map (fun m -> Busy <| sprintf "Redoing action: %s" m)
    let redoAction action pathFormat =
        Message <| (actionCompleteMessage action pathFormat |> sprintf "Action redone: %s")

    let noUndoActions = ErrorMessage "No more actions to undo"
    let noRedoActions = ErrorMessage "No more actions to redo"
    let cannotUndoNonEmptyCreated (node: Node) =
        ErrorMessage <| sprintf "Cannot undo creation of %s because it is no longer empty" node.Description
    let cannotUndoDelete permanent (node: Node) =
        ErrorMessage <| 
            if permanent then
                sprintf "Cannot undo deletion of %s" node.Description
            else
                sprintf "Cannot undo recycling of %s. Please open the Recycle Bin in Windows Explorer to restore this item" node.Description
