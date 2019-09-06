﻿module Koffee.MainLogicTests_FindSearch

open NUnit.Framework
open FsUnitTyped

let items names =
    let toItem name = {
        Name = name
        Path = Path.Root
        Type = Folder
        Modified = None
        Size = None
        IsHidden = false
        IsSearchMatch = false
    }
    names |> Seq.map toItem |> Seq.toList

let testModel cursorStart =
    { baseModel with
        Items = "alice,bob,charlie,crystal,apple,cherry".Split(',') |> items
        Cursor = cursorStart
    }

let assertEqualExceptCursor expected actual =
    assertAreEqualWith expected actual (ignoreMembers ["Cursor"; "Items"; "InputText"])

let doFind next prefix cursorStart =
    let model = { testModel cursorStart with LastFind = Some prefix }

    let actual =
        if next then
            MainLogic.Cursor.findNext model
        else
            MainLogic.Cursor.find prefix model

    let expected =
        { model with
            LastFind = Some prefix
            Status = if next then Some <| MainStatus.find prefix else None
        }
    assertEqualExceptCursor expected actual
    actual.Cursor

let find = doFind false
let findNext = doFind true

[<Test>]
let ``Find that matches nothing should not change the cursor``() =
    find "d" 1 |> shouldEqual 1

[<Test>]
let ``Find that matches only the current item should not change the cursor``() =
    find "b" 1 |> shouldEqual 1

[<Test>]
let ``Find that matches the current and next item should not change the cursor``() =
    find "c" 2 |> shouldEqual 2

[<Test>]
let ``Find that matches a item wrapping around should set the cursor to the that index``() =
    find "b" 2 |> shouldEqual 1

[<Test>]
let ``Find next that matches nothing should not change the cursor``() =
    findNext "d" 1 |> shouldEqual 1

[<Test>]
let ``Find next that matches only the current item should not change the cursor``() =
    findNext "b" 1 |> shouldEqual 1

[<Test>]
let ``Find next that matches the current and next item should set the cursor to the next index``() =
    findNext "c" 2 |> shouldEqual 3

[<Test>]
let ``Find next that matches a item wrapping around should set the cursor to the that index``() =
    findNext "b" 2 |> shouldEqual 1


type SearchResult = {
    Cursor: int
    Count: int
}
with
    static member fromModel (model: MainModel) =
        { Cursor = model.Cursor
          Count = model.Items |> Seq.filter (fun n -> n.IsSearchMatch) |> Seq.length
        }

let assertSearchGiven reverse searchStr cursorStart expectedResult =
    let search = if searchStr <> "" then Some (false, searchStr) else None
    let model = { testModel cursorStart with LastSearch = search }

    let actual = MainLogic.Cursor.searchNext reverse model

    let expected =
        { model with
            Status = search |> Option.map (fun (cs, s) -> MainStatus.search expectedResult.Count cs s)
        }
    SearchResult.fromModel actual |> shouldEqual expectedResult
    assertEqualExceptCursor expected actual

let assertSearch = assertSearchGiven false
let assertSearchPrevious = assertSearchGiven true

[<Test>]
let ``Search that matches nothing should not change the cursor``() =
    assertSearch "abc" 1 { Cursor = 1; Count = 0 }

[<Test>]
let ``Search that matches only the current item should not change the cursor``() =
    assertSearch "ob" 1 { Cursor = 1; Count = 1 }

[<Test>]
let ``Search that matches the current and next item should set the cursor to the next index``() =
    assertSearch "a" 2 { Cursor = 3; Count = 4 }

[<Test>]
let ``Search that matches a item wrapping around should set the cursor to the that index``() =
    assertSearch "ob" 2 { Cursor = 1; Count = 1 }

[<Test>]
let ``Search empy string matches nothing``() =
    assertSearch "" 2 { Cursor = 2; Count = 0 }


[<Test>]
let ``Search previous that matches nothing should not change the cursor``() =
    assertSearchPrevious "abc" 1 { Cursor = 1; Count = 0 }

[<Test>]
let ``Search previous that matches only the current item should not change the cursor``() =
    assertSearchPrevious "ob" 1 { Cursor = 1; Count = 1 }

[<Test>]
let ``Search previous that matches the current and previous item should set the cursor to the next index``() =
    assertSearchPrevious "a" 3 { Cursor = 2; Count = 4 }

[<Test>]
let ``Search previous that matches a item wrapping around should set the cursor to the that index``() =
    assertSearchPrevious "rys" 2 { Cursor = 3; Count = 1 }


let firstPart (s: string) = s.Split([|'/'|]).[0]

[<TestCase("")>]
[<TestCase("needle")>]
[<TestCase("hay stack/")>]
let ``parseSearch parses string without switch`` input =
    MainLogic.Cursor.parseSearch input |> shouldEqual (Ok (firstPart input, None))

[<TestCase("/i")>]
[<TestCase("hay stack/ic")>]
let ``parseSearch parses string with case-insensitive switch`` input =
    MainLogic.Cursor.parseSearch input |> shouldEqual (Ok (firstPart input, Some false))

[<TestCase("/c")>]
[<TestCase("hay stack/ci")>]
let ``parseSearch parses string with case-sensitive switch`` input =
    MainLogic.Cursor.parseSearch input |> shouldEqual (Ok (firstPart input, Some true))

[<TestCase("needle/a")>]
[<TestCase("needle/ia")>]
let ``parseSearch returns error for invalid switch`` input =
    MainLogic.Cursor.parseSearch input |> shouldEqual (Error <| InvalidSearchSwitch 'a')

[<TestCase("some/thing/")>]
[<TestCase("some/thing/i")>]
[<TestCase("some/other/thing/i")>]
let ``parseSearch returns error for more than one switch delimiter`` input =
    MainLogic.Cursor.parseSearch input |> shouldEqual (Error InvalidSearchSlash)
