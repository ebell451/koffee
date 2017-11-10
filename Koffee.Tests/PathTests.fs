﻿module Koffee.PathTests

open NUnit.Framework
open FsUnitTyped
open System.Reflection

let getPathValue (path: Path) =
    typedefof<Path>.GetProperty("Value", BindingFlags.NonPublic ||| BindingFlags.Instance)
                   .GetValue(path) :?> string

let shouldParseTo expectedStr path =
    path |> Option.map getPathValue
         |> shouldEqual (Some expectedStr)

[<TestCase("")>]
[<TestCase("/")>]
[<TestCase("drives")>]
[<TestCase("DRives")>]
let ``Parse returns root for all valid values`` input =
    input |> Path.Parse |> shouldEqual (Some Path.Root)

[<TestCase("c:")>]
[<TestCase(@"c:\")>]
[<TestCase("c:/")>]
[<TestCase("/c")>]
[<TestCase("/c/")>]
[<TestCase(@"/c\")>]
let ``Parse returns drive for valid drives`` input =
    input |> Path.Parse |> shouldParseTo @"C:\"

[<TestCase(@"c:\test")>]
[<TestCase(@"c:\test\")>]
[<TestCase("c:/test")>]
[<TestCase("c:/test/")>]
[<TestCase("/c/test")>]
[<TestCase("/c/test/")>]
[<TestCase(@"/c\test")>]
[<TestCase(@"/c\test\")>]
let ``Parse returns path for valid local paths`` input =
    input |> Path.Parse |> shouldParseTo @"C:\test"

[<TestCase(@"\\server", @"\\server")>]
[<TestCase(@"\\server\", @"\\server")>]
[<TestCase(@"/net/server", @"\\server")>]
[<TestCase(@"/net/server/", @"\\server")>]
[<TestCase(@"\\Serv_01\", @"\\Serv_01")>]
[<TestCase(@"/net/Serv_01/", @"\\Serv_01")>]
[<TestCase(@"\\Serv-R\", @"\\Serv-R")>]
[<TestCase(@"/net/Serv-R/", @"\\Serv-R")>]
[<TestCase(@"\\127.0.0.1\", @"\\127.0.0.1")>]
[<TestCase(@"/net/127.0.0.1/", @"\\127.0.0.1")>]
let ``Parse returns path for valid server path`` input expectedServerName =
    input |> Path.Parse |> shouldParseTo expectedServerName

[<TestCase(@"\\server\share")>]
[<TestCase(@"\\server\share\")>]
[<TestCase(@"//server/share")>]
[<TestCase(@"/net/server/share")>]
[<TestCase(@"/net/server/share/")>]
let ``Parse returns path for valid network paths`` input =
    input |> Path.Parse |> shouldParseTo @"\\server\share"

[<TestCase("~", "")>]
[<TestCase("~/", "")>]
[<TestCase("~/test", @"\test")>]
[<TestCase(@"~\", "")>]
[<TestCase(@"~\test", @"\test")>]
let ``Parse substitutes tilde for user directory`` input expectedSuffix =
    let expectedPath = Path.UserDirectory + expectedSuffix 
    input |> Path.Parse |> shouldParseTo expectedPath

[<TestCase("c")>]
[<TestCase("test")>]
[<TestCase("/test/")>]
[<TestCase("c:test")>]
[<TestCase(@"c\test\")>]
[<TestCase("/c:/test")>]
[<TestCase("/c/file?")>]
[<TestCase("/c/file*")>]
[<TestCase("/c/file<")>]
[<TestCase(@"\\")>]
[<TestCase("//")>]
[<TestCase(@"\\serv er")>]
[<TestCase("/net/serv er")>]
let ``Parse returns None for invalid paths`` input =
    input |> Path.Parse |> shouldEqual None


[<TestCase(@"", "/")>]
[<TestCase(@"C:\", "/c/")>]
[<TestCase(@"C:\test", "/c/test")>]
[<TestCase(@"C:\test\a folder", "/c/test/a folder")>]
[<TestCase(@"\\server", "/net/server")>]
[<TestCase(@"\\server\share", "/net/server/share")>]
let ``Format drive in Unix`` pathStr expected =
    match Path.Parse pathStr with
    | Some p -> p.Format Unix |> shouldEqual expected
    | None -> failwithf "Test path string '%s' does not parse" pathStr
