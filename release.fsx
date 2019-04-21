﻿(* Creates Koffee release zip and installer *)
#r "System.IO.Compression"
#r "System.IO.Compression.FileSystem"

open System
open System.IO
open System.IO.Compression
open System.Text.RegularExpressions
open System.Diagnostics

let joinPath a b = Path.Combine(a, b)
let getPath file = joinPath __SOURCE_DIRECTORY__ file

let hasExtension (extensions: string) (file: string) =
    extensions.Split(',') |> Seq.exists file.EndsWith

let lineSeq (reader: StreamReader) = seq {
    let mutable line = reader.ReadLine()
    while line <> null do
        yield line
        line <- reader.ReadLine()
}

let regexGroup pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then Some m.Groups.[1].Value
    else None

let versionRegex = @"\d\.\d\.\d"

let getVersion () =
    use reader = new StreamReader(getPath "RELEASE_NOTES.md")
    lineSeq reader
    |> Seq.choose (regexGroup (sprintf @"\#{3} (%s)" versionRegex))
    |> Seq.tryHead

let replaceInFile lineRegex regex (replacement: string) file =
    let lines =
        File.ReadAllLines(file)
        |> Seq.map (fun line ->
            if Regex.IsMatch(line, lineRegex) then Regex.Replace(line, regex, replacement)
            else line)
    File.WriteAllLines(file, lines)

let runProcess exe arguments =
    let p = new Process()
    p.StartInfo <- ProcessStartInfo(exe, arguments)
    p.StartInfo.UseShellExecute <- false
    p.Start() |> ignore
    p.WaitForExit()
    if p.ExitCode <> 0 then
        failwithf "Process failed: %s" (Path.GetFileName exe)

let copyFile destDir file =
    let dest = joinPath destDir (Path.GetFileName(file))
    File.Copy(file, dest)

let rec copyDir destDir dir =
    Directory.CreateDirectory destDir |> ignore
    Directory.EnumerateFiles(dir) |> Seq.iter (copyFile destDir)
    Directory.EnumerateDirectories(dir) |> Seq.iter (fun d ->
        let destDir = joinPath destDir (Path.GetFileName d)
        copyDir destDir d)

let backupFile file =
    if File.Exists(file) then
        let backup = file + ".bak"
        if File.Exists(backup) then
            File.Delete(backup)
        File.Move(file, backup)

try
    let distConfig = getPath "dist-config"

    // update version number
    let version =
        match getVersion() with
        | Some v -> v
        | None -> failwith "Could not read version from release notes"

    replaceInFile @"^\[<assembly: Assembly(File)?Version" versionRegex version (getPath @"Koffee\AssemblyInfo.fs")
    replaceInFile @"^AppVersion=|^OutputBaseFilename=" versionRegex version (joinPath distConfig "installer.iss")
    replaceInFile @"<version>|<iconUrl>|<licenseUrl>|<releaseNotes>" versionRegex version (joinPath distConfig "chocolatey\koffee.nuspec")

    Console.WriteLine(sprintf "Version set to %s" version)
    Console.WriteLine()

    // build
    let buildDir = getPath @"Koffee\bin\Release"
    if Directory.Exists buildDir then
        Directory.Delete(buildDir, true)
    runProcess @"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\msbuild.exe"
        ((getPath @"Koffee\Koffee.fsproj") + " /p:Configuration=Release /verbosity:normal")

    // TODO: run tests

    // setup binaries directory
    let filesDir = getPath "dist-files"
    let releaseDir = joinPath filesDir "Koffee"
    if Directory.Exists(filesDir) then
        Directory.Delete(filesDir, true)
    Directory.CreateDirectory(releaseDir) |> ignore
    let bins = Directory.EnumerateFiles(buildDir)
               |> Seq.filter (not << hasExtension ".pdb,.xml,.tmp")
    let docs = Directory.EnumerateFiles(getPath "")
               |> Seq.filter (hasExtension ".md,.txt")
    Seq.append bins docs |> Seq.iter (copyFile releaseDir)

    let distDir = getPath @"dist"
    Directory.CreateDirectory(distDir) |> ignore

    // create zip
    let zipFile = joinPath distDir (sprintf "Koffee-%s.zip" version)
    backupFile zipFile
    ZipFile.CreateFromDirectory(releaseDir, zipFile, CompressionLevel.Optimal, true)

    // create installer
    let setupFile = joinPath distDir (sprintf "Koffee-Setup-%s.exe" version)
    backupFile setupFile
    runProcess @"C:\Program Files (x86)\Inno Setup 5\iscc.exe"
        (sprintf "/Qp \"%s\"" (joinPath distConfig "installer.iss"))

    // create Chocolatey package
    backupFile (joinPath distDir (sprintf "koffee.%s.nupkg" version))
    let chocoDir = joinPath filesDir "chocolatey"
    copyDir chocoDir (joinPath distConfig "chocolatey")
    copyDir (joinPath chocoDir "tools") releaseDir
    runProcess "choco" (sprintf "pack %s\koffee.nuspec --out %s" chocoDir distDir)

    Console.WriteLine()
    Console.WriteLine(sprintf "Complete! Release files have been created in %s" distDir)
with | e -> Console.WriteLine(e.ToString())

Console.WriteLine()
Console.WriteLine("Press any key...")
Console.ReadKey()
