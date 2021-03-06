module ``Copy task``

open NUnit.Framework

open Xake
open Xake.Tasks

open System.IO

let TestOptions = {ExecOptions.Default with Threads = 1; Targets = ["main"]; ConLogLevel = Diag; FileLogLevel = Silent}

[<Test>]
let ``copies single file``() =
    "." </> ".xake" |> File.Delete
    if Directory.Exists "cptgt" then
        Directory.Delete ("cptgt", true)

    do xake TestOptions {
        rules [
            "main" => action {
                do! need ["samplefile"]
                do! Cp {CpArgs.Default with file = "samplefile"; todir = "cptgt"}
            }

            "samplefile" ..> writeText "hello world"
        ]
    }

    Assert.True <| File.Exists ("cptgt" </> "samplefile")

[<Test>]
let ``copies folder flatten``() =
    "." </> ".xake" |> File.Delete
    ["cptgt"; "cpin"] |> List.map (fun d -> if Directory.Exists d then Directory.Delete (d, true))

    do xake TestOptions {
        rules [
            "main" => action {
                do! need ["cpin/samplefile"]
                do! Cp {CpArgs.Default with dir = "cpin"; todir = "cptgt"; flatten = true}
            }

            "cpin/samplefile" ..> writeText "hello world"
        ]
    }

    Assert.True <| File.Exists ("cptgt" </> "samplefile")

[<Test>]
let ``copies folder no flatten``() =
    "." </> ".xake" |> File.Delete
    ["cptgt"; "cpin"] |> List.map (fun d -> if Directory.Exists d then Directory.Delete (d, true))

    do xake TestOptions {
        rules [
            "main" => action {
                do! need ["cpin/a/samplefile"]
                do! Cp {CpArgs.Default with dir = "cpin"; todir = "cptgt"; flatten = false}
            }

            "cpin/a/samplefile" ..> writeText "hello world"
        ]
    }

    Assert.True <| File.Exists ("cptgt" </> "cpin" </> "a" </> "samplefile")

[<Test>]
let ``copies fileset NO flatten``() =
    "." </> ".xake" |> File.Delete
    ["cptgt"; "cpin"] |> List.map (fun d -> if Directory.Exists d then Directory.Delete (d, true))

    do xake TestOptions {
        rules [
            "main" => action {
                do! need ["cpin/a/samplefile"]
                do! Cp {CpArgs.Default with
                    files = (fileset {basedir "cpin"; includes "**/*"})
                    todir = "cptgt"
                    flatten = false
                    }
            }

            "cpin/a/samplefile" ..> writeText "hello world"
        ]
    }

    Assert.True <| File.Exists ("cptgt" </> "a" </> "samplefile")

[<Test>]
let ``copies fileset flatten``() =
    "." </> ".xake" |> File.Delete
    ["cptgt"; "cpin"] |> List.map (fun d -> if Directory.Exists d then Directory.Delete (d, true))

    do xake TestOptions {
        rules [
            "main" => action {
                do! need ["cpin/a/samplefile"]
                do! cp {files !!"cpin/**/*"; todir "cptgt"; flatten}
            }

            "cpin/a/samplefile" ..> writeText "hello world"
        ]
    }

    Assert.True <| File.Exists ("cptgt" </> "samplefile")
