﻿namespace Xake

module BuildLog = 
    open Xake
    open System
    
    let XakeVersion = "0.3"
    
    type Database = { Status : Map<Target, BuildResult> }
    
    (* API *)

    /// Creates a new build result
    let makeResult target = 
        { Result = target
          Built = DateTime.Now
          Depends = []
          Steps = [] }
    
    /// Creates a new database
    let newDatabase() = { Database.Status = Map.empty }
    
    /// Adds result to a database
    let internal addResult db result = 
        { db with Status = db.Status |> Map.add (result.Result) result }

type Agent<'t> = MailboxProcessor<'t>

module Storage = 
    open Xake
    open BuildLog
    
    module private Persist = 
        open System
        open Pickler

        type DatabaseHeader = 
            { XakeSign : string
              XakeVer : string
              ScriptDate : Timestamp }
        
        let artifact = wrap (newArtifact, fun a -> a.Name) str
        
        let target = 
            alt (function 
                | FileTarget _ -> 0
                | PhonyAction _ -> 1) 
                [| wrap (newArtifact >> FileTarget, fun (FileTarget f) -> f.Name) 
                       str
                   wrap (PhonyAction, (fun (PhonyAction a) -> a)) str |]
        
        let step = 
            wrap 
                ((fun (n, s, o, w) -> {StepInfo.Name = n; Start = s; OwnTime = o * 1<ms>; WaitTime = w * 1<ms>}), 
                 fun ({StepInfo.Name = n; Start = s; OwnTime = o; WaitTime = w}) -> (n, s, o / 1<ms>, w / 1<ms>)) (quad str date int int)
        
        // Fileset of FilesetOptions * FilesetElement list
        let dependency = 
            alt (function 
                | ArtifactDep _ -> 0
                | File _ -> 1
                | EnvVar _ -> 2
                | Var _ -> 3
                | AlwaysRerun _ -> 4
                | GetFiles _ -> 5) 
                [| wrap (ArtifactDep, fun (ArtifactDep f) -> f) target
                   
                   wrap (File, fun (File(f, ts)) -> (f, ts)) 
                       (pair artifact date)
                   
                   wrap (EnvVar, fun (EnvVar(n, v)) -> n, v) 
                       (pair str (option str))
                   wrap (Var, fun (Var(n, v)) -> n, v) (pair str (option str))
                   wrap0 AlwaysRerun
                   
                   wrap (GetFiles, fun (GetFiles(fs, fi)) -> fs, fi) 
                       (pair filesetPickler filelistPickler) |]
        
        let result = 
            wrap 
                ((fun (r, built, deps, steps) -> 
                 { Result = r
                   Built = built
                   Depends = deps
                   Steps = steps }), 
                 fun r -> (r.Result, r.Built, r.Depends, r.Steps)) 
                (quad target date (list dependency) (list step))
        
        let dbHeader = 
            wrap 
                ((fun (sign, ver, scriptDate) -> 
                 { DatabaseHeader.XakeSign = sign
                   XakeVer = ver
                   ScriptDate = scriptDate }), 
                 fun h -> (h.XakeSign, h.XakeVer, h.ScriptDate)) 
                (triple str str date)
    
    module private impl = 
        open System.IO
        open Persist
        
        let writeHeader w = 
            let h = 
                { DatabaseHeader.XakeSign = "XAKE"
                  XakeVer = XakeVersion
                  ScriptDate = System.DateTime.Now }
            Persist.dbHeader.pickle h w
        
        let openDatabaseFile path (logger : ILogger) = 
            let log = logger.Log
            let resultPU = Persist.result
            let dbpath, bkpath = path </> ".xake", path </> ".xake" <.> "bak"
            // if exists backup restore
            if File.Exists(bkpath) then 
                log Level.Message "Backup file found ('%s'), restoring db" 
                    bkpath
                try 
                    File.Delete(dbpath)
                with _ -> ()
                File.Move(bkpath, dbpath)
            let db = ref (newDatabase())
            let recordCount = ref 0
            // read database
            if File.Exists(dbpath) then 
                try 
                    use reader = new BinaryReader(File.OpenRead(dbpath))
                    let stream = reader.BaseStream
                    let header = Persist.dbHeader.unpickle reader
                    if header.XakeVer < XakeVersion then 
                        failwith "Database version is old."
                    while stream.Position < stream.Length do
                        let result = resultPU.unpickle reader
                        db := result |> addResult !db
                        recordCount := !recordCount + 1
                // if fails create new
                with ex -> 
                    log Level.Error 
                        "Failed to read database, so recreating. Got \"%s\"" 
                    <| ex.ToString()
                    try 
                        File.Delete(dbpath)
                    with _ -> ()
            // check if we can cleanup db
            if !recordCount > (!db).Status.Count * 5 then 
                log Level.Message "Compacting database"
                File.Move(dbpath, bkpath)
                use writer = 
                    new BinaryWriter(File.Open(dbpath, FileMode.CreateNew))
                writeHeader writer
                (!db).Status
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.iter (fun r -> resultPU.pickle r writer)
                File.Delete(bkpath)
            let dbwriter = 
                new BinaryWriter(File.Open (dbpath, FileMode.Append, FileAccess.Write))
            if dbwriter.BaseStream.Position = 0L then writeHeader dbwriter
            db, dbwriter
    
    type DatabaseApi = 
        | GetResult of Target * AsyncReplyChannel<Option<BuildResult>>
        | Store of BuildResult
        | Close
        | CloseWait of AsyncReplyChannel<unit>
    
    /// <summary>
    /// Build result pickler.
    /// </summary>
    let resultPU = Persist.result
    
    /// <summary>
    /// Opens database.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="logger"></param>
    let openDb path (logger : ILogger) = 
        let db, dbwriter = impl.openDatabaseFile path logger
        MailboxProcessor.Start(fun mbox -> 
            let rec loop (db) = 
                async { 
                    let! msg = mbox.Receive()
                    match msg with
                    | GetResult(key, chnl) -> 
                        db.Status
                        |> Map.tryFind key
                        |> chnl.Reply
                        return! loop (db)
                    | Store result -> 
                        Persist.result.pickle result dbwriter
                        return! loop (result |> addResult db)
                    | Close -> 
                        logger.Log Info "Closing database"
                        dbwriter.Dispose()
                        return ()
                    | CloseWait ch -> 
                        logger.Log Info "Closing database"
                        dbwriter.Dispose()
                        ch.Reply()
                        return ()
                }
            loop (!db))

/// Utility methods to manipulate build stats
module internal Step =

    let start name = {StepInfo.Empty with Name = name; Start = System.DateTime.Now}

    /// <summary>
    /// Updated last (current) build step
    /// </summary>
    let updateLastStep fn = function
        | {Steps = current :: rest} as result -> {result with Steps = (fn current) :: rest}
        | _ as result -> result

    /// <summary>
    /// Adds specific amount to a wait time
    /// </summary>
    let updateWaitTime delta = updateLastStep (fun c -> {c with WaitTime = c.WaitTime + delta})
    let updateTotalDuration =
        let durationSince (startTime: System.DateTime) = int (System.DateTime.Now - startTime).TotalMilliseconds * 1<ms>
        updateLastStep (fun c -> {c with OwnTime = (durationSince c.Start) - c.WaitTime})
    let lastStep = function
        | {Steps = current :: rest} -> current
        | _ -> start "dummy"
