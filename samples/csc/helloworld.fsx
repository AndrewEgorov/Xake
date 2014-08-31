// xake build file
#r @"../../bin/Xake.Core.dll"

open Xake

do xake XakeOptions {

  want (["hw.exe"])

  rule("hw.exe" *> fun exe -> action {
    do! Csc {
      CscSettings with
        Out = exe
        Src = !! "a.cs"
      }
    })
}