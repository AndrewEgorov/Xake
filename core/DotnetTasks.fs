﻿namespace Xake

open System.IO
open Xake

[<AutoOpen>]
module DotnetTasks =

  type FrameworkInfo = {Version: string; InstallPath: string}

  let tryLocateFwk name : option<FrameworkInfo> =
    let fwkRegKey =
      match name with
      //      | "1.1" -> "v1.1.4322"
      //      | "1.1" -> "v1.1.4322"
      //      | "2.0" -> "v2.0.50727"
      //      | "3.0" -> "v3.0\Setup\InstallSuccess 
      | "net-35" | "3.5" -> "v3.5"
      | "net-40c" | "4.0-client" -> "v4\\Client"
      | "net-40" | "4.0"| "4.0-full" -> "v4\\Full"
      | _ -> failwithf "Unknown or unsupported profile '%s'" name

    let ndp = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP")
    if ndp = null then None
    else
      let fwk = ndp.OpenSubKey(fwkRegKey)
      if fwk = null || not (fwk.GetValue("Install").Equals(1)) then
        None
      else
        Some {InstallPath = fwk.GetValue("InstallPath") :?> string; Version = fwk.GetValue("Version") :?> string}

  // attempts to locate framework, fails if not found
  let locateFwk name =
    match tryLocateFwk name with
    | Some i -> i
    | _ -> failwithf ".NET framework '%s' not found" name

  // attempts to locate any framework
  let locateFwkAny() =
    let ipath n =
      match tryLocateFwk n with
      | Some i -> i.InstallPath
      | None -> null
    
    match ["3.5"; "4.0"] |> List.fold (fun a p -> if a <> null then a else ipath p) null with
      | null -> failwith "No framework found"
      | fi -> fi
    
  // CSC task and related types
  type TargetType = |AppContainerExe |Exe |Library |Module |WinExe |WinmdObj
  type TargetPlatform = |AnyCpu |AnyCpu32Preferred |ARM | X64 | X86 |Itanium
  type CscSettingsType =
    {
      Platform: TargetPlatform;
      Target: TargetType;
      OutFile: Artifact;
      SrcFiles: FilesetType;
      References: FilesetType;
      Resources: FilesetType;
      Define: string list;

      FailOnError: bool
    }
  // see http://msdn.microsoft.com/en-us/library/78f4aasd.aspx
  // defines, optimize, warn, debug, platform

  let CscSettings = {Platform = AnyCpu; Target = Exe; OutFile = null; SrcFiles = Fileset.Empty; References = Fileset.Empty; Resources = Fileset.Empty; Define = []; FailOnError = true}

  let mutable private iid_lock = System.Object()
  let mutable private iid = 0

  let internal newProcPrefix () = lock iid_lock (fun _ ->
    let pfx = sprintf "[CSC%i]" iid in 
    iid <- iid + 1
    pfx)

  /// C# compiler task
  let Csc settings =
    
    let targetStr = function |AppContainerExe -> "appcontainerexe" |Exe -> "exe" |Library -> "library" |Module -> "module" |WinExe -> "winexe" |WinmdObj -> "winmdobj"
    let platformStr = function |AnyCpu -> "anycpu" |AnyCpu32Preferred -> "anycpu32preferred" |ARM -> "arm" | X64 -> "x64" | X86 -> "x86" |Itanium -> "itanium"

    let pfx = newProcPrefix()

    async {

      do log Level.Info "%s starting '%s'" pfx settings.OutFile.Name
      
      let (FileList src) = scan settings.SrcFiles
      let (FileList refs) = scan settings.References
      let (FileList ress) = scan settings.Resources
      do! need (src @ refs @ ress)

      let args =
        seq {
          yield "/nologo"

          yield "/target:" + targetStr settings.Target
          yield "/platform:" + platformStr settings.Platform

          if settings.OutFile <> null then
            yield sprintf "/out:%s" settings.OutFile.FullName

          if List.exists (fun _ -> true) settings.Define then
            yield "/define:" + System.String.Join(";", Array.ofList settings.Define)

          yield! src |> List.map fullname
          yield! refs |> List.map (fullname >> (+) "/r:")
          // TODO resources
        }

      let commandLine = args |> escapeAndJoinArgs
      let csc_exe = Path.Combine(locateFwkAny(), "csc.exe")

      let! exitCode = _system (pfx + " ") csc_exe commandLine

      do log Level.Info "%s completed '%s'" pfx settings.OutFile.Name
      if exitCode <> 0 then
        do log Level.Error "%s failed with exit code '%i'" pfx exitCode
        if settings.FailOnError then failwith "Exiting due to FailOnError set"
    }
