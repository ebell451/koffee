﻿namespace Koffee

open System
open FSharp.Desktop.UI

type Path =
    | Path of string
    member this.Value =
        let (Path p) = this
        p

type NodeType =
    | File
    | Folder
    | Drive
    | Error

type Node = {
    Path: Path
    Name: string
    Type: NodeType
    Modified: DateTime option
    Size: int64 option
}

type Node with
    member this.SizeFormatted =
        if this.Size.IsSome then
            let scale level = pown 1024L level
            let scaleCutoff level = 10L * (scale level)
            let scaledStr size level =
                let scaled = size / (scale level)
                let levelName = "KB,MB,GB".Split(',').[level-1]
                scaled.ToString("N0") + " " + levelName
            match this.Size.Value with
                | size when size > scaleCutoff 3 -> scaledStr size 3
                | size when size > scaleCutoff 2 -> scaledStr size 2
                | size when size > scaleCutoff 1 -> scaledStr size 1
                | size -> size.ToString("N0")
        else ""

[<AbstractClass>]
type MainModel() =
    inherit Model()

    abstract Path: Path with get, set
    abstract Status: string with get, set
    abstract Nodes: Node list with get, set
    abstract Cursor: int with get, set
    abstract PageSize: int with get, set
    abstract LastFind: char option with get, set
    abstract LastSearch: string option with get, set

    member this.SelectedNode = this.Nodes.[this.Cursor]
    member this.HalfPageScroll = this.PageSize/2 - 1

type CommandInput =
    | FindInput
    | SearchInput

type MainEvents =
    | CursorUp
    | CursorDown
    | CursorUpHalfPage
    | CursorDownHalfPage
    | CursorToFirst
    | CursorToLast
    | OpenPath of string
    | OpenSelected
    | OpenParent
    | OpenExplorer
    | StartInput of CommandInput
    | Find of char
    | FindNext
    | Search of string
    | SearchNext
    | OpenSettings
    | TogglePathFormat

    member this.FriendlyName =
        match this with
        | CursorUp -> "Move Cursor Up"
        | CursorDown -> "Move Cursor Down"
        | CursorUpHalfPage -> "Move Cursor Up Half Page"
        | CursorDownHalfPage -> "Move Cursor Down Half Page"
        | CursorToFirst -> "Move Cursor to First Item"
        | CursorToLast -> "Move Cursor to Last Item"
        | OpenPath path -> sprintf "Open Path \"%s\"" path
        | OpenSelected -> "Open Selected Item"
        | OpenParent -> "Open Parent Folder"
        | OpenExplorer -> "Open Windows Explorer at Current Location"
        | StartInput FindInput -> "Find Item Beginning With Character"
        | StartInput SearchInput -> "Search For Items"
        | Find char -> sprintf "Find Item Beginning With \"%c\"" char
        | FindNext -> "Go To Next Find Match"
        | Search str -> sprintf "Search For Items Matching \"%s\"" str
        | SearchNext -> "Go To Next Search Match"
        | OpenSettings -> "Open Help/Settings"
        | TogglePathFormat -> "Toggle Between Windows and Unix Path Format"

    static member Bindable = [
        CursorUp
        CursorDown
        CursorUpHalfPage
        CursorDownHalfPage
        CursorToFirst
        CursorToLast
        OpenSelected
        OpenParent
        StartInput FindInput
        FindNext
        StartInput SearchInput
        SearchNext
        OpenSettings
        OpenExplorer
        TogglePathFormat
    ]
