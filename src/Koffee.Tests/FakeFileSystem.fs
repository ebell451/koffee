﻿namespace Koffee

open System.Collections.Generic
open Acadian.FSharp

type TreeItem =
    | TreeFile of string * (Item -> Item)
    | TreeFolder of string * TreeItem list
with
    static member build items =
        let rec build (path: Path) items =
            items |> List.collect (fun item ->
                match item with
                | TreeFile (name, transform) ->
                    Item.Basic (path.Join name) name File |> transform |> List.singleton
                | TreeFolder (name, items) ->
                    let folder = Item.Basic (path.Join name) name Folder
                    folder :: build folder.Path items
            )
        let c = Path.Parse "/c" |> Option.get
        Item.Basic c "C" Folder :: build c items

type FakeFileSystem(treeItems) =
    let mutable items = treeItems |> TreeItem.build
    let shortcuts = Dictionary<Path, string>()
    let mutable recycleBin = []
    let exnPaths = Dictionary<Path, exn list>()
    let mutable callsToGetItems = 0

    let remove path =
        items <- items |> List.filter (fun i -> i.Path <> path)
        shortcuts.Remove(path) |> ignore

    let checkPathNotUsed path =
        if items |> List.exists (fun i -> i.Path = path) then
            Error (exn ("Path already used: " + string path))
        else
            Ok ()

    let checkExn path =
        match exnPaths.TryGetValue path with
        | true, e::exns ->
            if exns |> List.isEmpty then
                exnPaths.Remove path |> ignore
            else
                exnPaths.[path] <- exns
            Error e
        | _ ->
            Ok ()

    member this.Items = items |> List.sortBy (fun i -> string i.Path)

    member this.ItemsIn path =
        items |> List.filter (fun i -> i.Path.Parent = path)

    member this.Item path =
        items |> List.find (fun i -> i.Path = path)

    member this.RecycleBin = recycleBin

    member this.AddExnPath e path =
        match exnPaths.TryGetValue path with
        | true, exns ->
            exnPaths.[path] <- exns @ [e]
        | _ ->
            exnPaths.Add(path, [e])

    member this.CallsToGetItems = callsToGetItems

    interface IFileSystem

    interface IFileSystemReader with
        member this.GetItem path = this.GetItem path
        member this.GetItems path = this.GetItems path
        member this.GetFolders path = this.GetFolders path
        member this.IsEmpty path = this.IsEmpty path
        member this.GetShortcutTarget path = this.GetShortcutTarget path

    interface IFileSystemWriter with
        member this.Create itemType path = this.Create itemType path
        member this.CreateShortcut target path = this.CreateShortcut target path
        member this.Move fromPath toPath = this.Move fromPath toPath
        member this.Copy fromPath toPath = this.Copy fromPath toPath
        member this.Recycle path = this.Recycle path
        member this.Delete path = this.Delete path

    member this.GetItem path = result {
        do! checkExn path
        return items |> List.tryFind (fun i -> i.Path = path)
    }

    member private this.AssertItem path = result {
        let! item = this.GetItem path
        let! item = item |> Result.ofOption (exn ("Path does not exist: " + (string path)))
        return item
    }

    member this.GetItems path = result {
        callsToGetItems <- callsToGetItems + 1
        do! checkExn path
        return this.Items |> List.filter (fun i -> i.Path.Parent = path)
    }

    member this.GetFolders path =
        this.GetItems path |> Result.map (
            List.filter (fun i -> i.Type = Folder)
            >> List.sortBy (fun i -> i.Name.ToLower())
        )

    member this.IsEmpty path =
        match items |> List.tryFind (fun i -> i.Path = path) with
        | Some i when i.Type = Folder -> not (items |> List.exists (fun i -> i.Path.Parent = path))
        | Some i when i.Type = File -> not (i.Size |> Option.exists (flip (>) 0L))
        | _ -> false

    member this.GetShortcutTarget path = result {
        do! checkExn path
        match shortcuts.TryGetValue path with
        | true, p -> return p
        | _ -> return! Error (exn "Not a shortcut")
    }


    member this.Create itemType path = result {
        do! checkExn path
        do! checkPathNotUsed path
        items <- Item.Basic path path.Name itemType :: items
    }

    member this.CreateShortcut target path = result {
        remove path
        do! this.Create File path
        shortcuts.Add(path, string target)
    }

    member this.Move fromPath toPath = result {
        if String.equalsIgnoreCase (string fromPath) (string toPath) then
            let! item = this.AssertItem fromPath
            let newItem = { item with Path = toPath; Name = toPath.Name }
            items <- items |> List.map (fun i -> if i.Path = fromPath then newItem else i)
        else
            do! this.Copy fromPath toPath
            do! this.Delete fromPath
    }

    member this.Copy fromPath toPath = result {
        let! item = this.AssertItem fromPath
        do! checkExn toPath
        remove toPath
        let newItem = { item with Path = toPath; Name = toPath.Name }
        items <- newItem :: items
        match shortcuts.TryGetValue fromPath with
        | true, target -> shortcuts.Add(toPath, target)
        | _ -> ()
    }

    member this.Recycle path = result {
        let! item = this.AssertItem path
        recycleBin <- item :: recycleBin
        return! this.Delete path
    }

    member this.Delete path = result {
        let! _ = this.AssertItem path
        remove path
    }
