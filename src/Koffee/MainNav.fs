module Koffee.Main.Nav

open FSharp.Control
open Acadian.FSharp
open Koffee
open Koffee.Main.Util

type ShortcutTargetMissingException(itemName, targetPath) =
    inherit exn(sprintf "Shortcut target for %s does not exist: %s" itemName targetPath)

let moveCursor cursor (model: MainModel) =
    model |> MainModel.withCursor (
        match cursor with
        | CursorStay -> model.Cursor
        | CursorToIndex index -> index
        | CursorToName name ->
            let matchFunc =
                if name.Length = 2 && name.[1] = ':'
                then String.startsWithIgnoreCase // if name is drive, use "starts with" because drives have labels
                else String.equalsIgnoreCase
            model.Items |> List.tryFindIndex (fun i -> matchFunc name i.Name) |? model.Cursor
        | CursorToItem (item, _) ->
            model.Items |> List.tryFindIndex (fun i -> i.Path = item.Path) |? model.Cursor
    )

let listDirectory cursor model =
    let hiddenCursorItem =
        match cursor with
        | CursorToItem (item, true) -> Some item
        | _ -> None
    let items =
        model.Directory
        |> List.filter (fun i -> model.Config.ShowHidden || not i.IsHidden || Some i = hiddenCursorItem)
        |> (model.Sort |> Option.map SortField.SortByTypeThen |? id)
        |> model.ItemsOrEmpty
    { model with Items = items } |> moveCursor cursor

let private getDirectory (fsReader: IFileSystemReader) (model: MainModel) path =
    if path = Path.Network then
        model.History.NetHosts
        |> List.map (sprintf @"\\%s")
        |> List.choose Path.Parse
        |> List.map (fun path -> Item.Basic path path.Name NetHost)
        |> Ok
    else
        fsReader.GetItems path
        |> Result.mapError (fun e -> MainStatus.CouldNotOpenPath (path, model.PathFormat, e))

let openPath (fsReader: IFileSystemReader) path cursor (model: MainModel) =
    match getDirectory fsReader model path with
    | Ok directory ->
        { model with
            Directory = directory
            History = model.History |> History.withFolderPath model.Config.Limits.PathHistory path
            Sort = Some (model.History.FindSortOrDefault path |> PathSort.toTuple)
        }
        |> MainModel.withPushedLocation path
        |> clearSearchProps
        |> listDirectory cursor
        |> Ok
    | Error e when path = model.Location ->
        let items = Item.EmptyFolderWithMessage e.Message path
        { model with Directory = items; Items = items }
        |> MainModel.withError e
        |> Ok
    | Error e ->
        Error e

let openUserPath (fsReader: IFileSystemReader) pathStr (model: MainModel) =
    match Path.Parse pathStr with
    | Some path ->
        match fsReader.GetItem path with
        | Ok (Some item) when item.Type = File ->
            model |> MainModel.clearStatus |> openPath fsReader path.Parent (CursorToItem (item, true))
        | Ok _ ->
            model |> MainModel.clearStatus |> openPath fsReader path CursorStay
        | Error e ->
            Error <| MainStatus.ActionError ("open path", e)
    | None ->
        Error <| MainStatus.InvalidPath pathStr

let openInputPath fsReader os pathStr (evtHandler: EvtHandler) model = result {
    let pathStr = OsUtility.subEnvVars os pathStr
    let! model = openUserPath fsReader pathStr model
    evtHandler.Handle ()
    return model
}

let getOpenTarget (fsReader: IFileSystemReader) pathFormat (item: Item) =
    match item.Type with
    | File ->
        let shortcutFolder = result {
            let! targetPath =
                if item.Path.Extension |> String.equalsIgnoreCase "lnk" then
                    fsReader.GetShortcutTarget item.Path
                    |> Result.map Path.Parse
                else
                    Ok None
            match targetPath with
            | Some targetPath ->
                let! targetOpt = fsReader.GetItem targetPath
                let! target =
                    targetOpt |> Result.ofOption (
                        ShortcutTargetMissingException(item.Name, targetPath.Format pathFormat) :> exn
                    )
                return if target.Type = Folder then Some (Folder, target.Path) else None
            | None ->
                return None
        }
        shortcutFolder |> Result.map (Option.defaultValue (item.Type, item.Path))
    | _ ->
        Ok (item.Type, item.Path)

let openSelected fsReader (os: IOperatingSystem) (model: MainModel) = asyncSeqResult {
    let getOpenTarget item =
        getOpenTarget fsReader model.PathFormat item |> Result.mapError (fun ex -> item.Name, ex)
    let openFile path =
        os.OpenFile path
        |> Result.map (fun () -> path)
        |> Result.mapError (fun ex -> path.Name, ex)
    match model.ActionItems with
    | [item] ->
        let mapError res = res |> Result.mapError (fun (name, ex) -> MainStatus.CouldNotOpenFiles [name, ex])
        let! itemType, path = getOpenTarget item |> mapError
        match itemType with
        | Folder | Drive | NetHost | NetShare ->
            yield! model |> MainModel.clearStatus |> openPath fsReader path CursorStay
        | File ->
            do! openFile item.Path |> mapError |> Result.map ignore
            yield
                model
                |> MainModel.mapHistory (History.withFilePaths model.Config.Limits.PathHistory [item.Path])
                |> MainModel.withMessage (MainStatus.OpenFiles [item.Name])
        | Empty -> ()
    | items ->
        let targets, targetErrors =
            items
            |> List.map getOpenTarget
            |> Result.partition
        let opened, openErrors =
            targets
            |> Seq.filter (fst >> (=) File)
            |> Seq.map (snd >> openFile)
            |> Result.partition
        let model = model |> MainModel.mapHistory (History.withFilePaths model.Config.Limits.PathHistory opened)
        match targetErrors @ openErrors with
        | [] ->
            if opened.IsEmpty then
                return MainStatus.NoFilesSelected
            else
                yield model |> MainModel.withMessage (MainStatus.OpenFiles (opened |> List.map (fun p -> p.Name)))
        | errors ->
            yield model
            return MainStatus.CouldNotOpenFiles errors
}

let openParent fsReader (model: MainModel) =
    if model.SearchCurrent.IsSome then
        if model.CursorItem.Type = Empty then
            Ok (model |> clearSearchProps |> listDirectory CursorStay)
        else
            model
            |> MainModel.clearStatus
            |> openPath fsReader model.CursorItem.Path.Parent (CursorToItem (model.CursorItem, false))
    else
        let rec getParent n (path: Path) (currentName: string) =
            if n < 1 || path = Path.Root then (path, currentName)
            else getParent (n-1) path.Parent path.Name
        let path, cursorName = getParent model.RepeatCount model.Location model.Location.Name
        model |> MainModel.clearStatus |> openPath fsReader path (CursorToName cursorName)

let refresh fsReader (model: MainModel) =
    openPath fsReader model.Location (CursorToItem (model.CursorItem, false)) model

let refreshDirectory fsReader (model: MainModel) =
    getDirectory fsReader model model.Location
    |> function
        | Ok items -> items
        | Error e -> Item.EmptyFolderWithMessage e.Message model.Location
    |> fun items -> { model with Directory = items }

let rec private shiftStacks n current fromStack toStack =
    match fromStack with
    | newCurrent :: fromStack when n > 0 ->
        shiftStacks (n-1) newCurrent fromStack (current :: toStack)
    | _ ->
        (current, fromStack, toStack)

let private removeIndexOrLast index list =
    let index = min index (List.length list - 1)
    list |> Seq.indexed |> Seq.filter (fst >> (<>) index) |> Seq.map snd |> Seq.toList

let back fsReader model =
    if model.BackStack = [] then
        model
    else
        let (path, cursor), fromStack, toStack =
            shiftStacks model.RepeatCount (model.Location, model.Cursor) model.BackStack model.ForwardStack
        model
        |> MainModel.clearStatus
        |> openPath fsReader path (CursorToIndex cursor)
        |> function
            | Ok model ->
                { model with BackStack = fromStack; ForwardStack = toStack }
            | Error e ->
                { model with BackStack = model.BackStack |> removeIndexOrLast (model.RepeatCount-1) }
                |> MainModel.withError e

let forward fsReader model =
    if model.ForwardStack = [] then
        model
    else
        let (path, cursor), fromStack, toStack =
            shiftStacks model.RepeatCount (model.Location, model.Cursor) model.ForwardStack model.BackStack
        model
        |> MainModel.clearStatus
        |> openPath fsReader path (CursorToIndex cursor)
        |> function
            | Ok model -> { model with BackStack = toStack; ForwardStack = fromStack }
            | Error e ->
                { model with ForwardStack = model.ForwardStack |> removeIndexOrLast (model.RepeatCount-1) }
                |> MainModel.withError e

let sortList field model =
    let desc =
        match model.Sort with
        | Some (f, desc) when f = field -> not desc
        | _ -> field = Modified
    let cursor = if model.Cursor = 0 then CursorStay else CursorToItem (model.CursorItem, false)
    { model with
        Sort = Some (field, desc)
        Items = model.Items |> SortField.SortByTypeThen (field, desc)
        History = model.History |> History.withPathSort model.Location { Sort = field; Descending = desc }
    }
    |> MainModel.withMessage (MainStatus.Sort (field, desc))
    |> moveCursor cursor

let suggestPaths (fsReader: IFileSystemReader) (model: MainModel) = asyncSeq {
    let getSuggestions search (paths: HistoryPath list) =
        paths |> filterByTerms true false search (fun p -> p.PathValue.Name)
    let dirAndSearch =
        model.LocationInput
        |> String.replace @"\" "/"
        |> String.lastIndexOf "/"
        |> Option.ofCond (flip (>=) 0)
        |> Option.map (fun i ->
            let dir = model.LocationInput |> String.substring 0 i
            let search = model.LocationInput |> String.substringFrom (i + 1)
            (dir, search)
        )
    match dirAndSearch with
    | Some (dir, search) ->
        match dir |> Path.Parse with
        | Some dir ->
            let! pathsRes =
                match model.PathSuggestCache with
                | Some (cachePath, cache) when cachePath = dir -> async { return cache }
                | _ -> runAsync (fun () ->
                    fsReader.GetFolders dir
                    |> Result.map Item.paths
                    |> Result.mapError (fun e -> e.Message)
                )
            let toHistory p = { PathValue = p; IsDirectory = true }
            let suggestions = pathsRes |> Result.map (List.map toHistory >> getSuggestions search)
            yield { model with PathSuggestions = suggestions; PathSuggestCache = Some (dir, pathsRes) }
        | None ->
            yield { model with PathSuggestions = Ok [] }
    | None when model.LocationInput |> String.isNotEmpty ->
        let suggestions = getSuggestions model.LocationInput model.History.Paths
        yield { model with PathSuggestions = Ok suggestions }
    | None ->
        yield { model with PathSuggestions = Ok [] }
}

let deletePathSuggestion (path: HistoryPath) (model: MainModel) =
    // if location input does not contain a slash, the suggestions are from history
    if not (model.LocationInput |> String.contains "/" || model.LocationInput |> String.contains @"\") then
        { model with
            History = { model.History with Paths = model.History.Paths |> List.filter ((<>) path) }
            PathSuggestions = model.PathSuggestions |> Result.map (List.filter ((<>) path))
        }
    else model

let scrollView (gridScroller: DataGridScroller) scrollType (model: MainModel) =
    let topIndex =
        match scrollType with
        | CursorTop -> model.Cursor
        | CursorMiddle -> model.Cursor - (model.PageSize/2)
        | CursorBottom -> model.Cursor - model.PageSize + 1
    // unfortunately, a two-way binding on scroll index is not feasible because the ScrollViewer does not exist at
    // binding time, so we're using a side effect :(
    gridScroller.ScrollTo topIndex
    model
