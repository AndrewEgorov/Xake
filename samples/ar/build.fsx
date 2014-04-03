// xake build file for activereports source code

#r @"..\..\bin\Xake.Core.dll"
open Xake

////// utility methods
let ardll name = "out\\GrapeCity.ActiveReports." + name + ".v9.dll"
let arexe name = "out\\GrapeCity.ActiveReports." + name + ".v9.exe"
let ardep = List.map (ardll >> (~&)) >> FileList

let commonSrcFiles = fileset {
  includes "CommonAssemblyInfo.cs"
  includes "VersionInfo.cs"
  includes "AssemblyNames.cs"
  includes "CommonFiles/SmartAssembly.Attributes.cs"
  }

// TODO consider making filesets
module libs =
  let nunit = &"Tools/NUnit/nunit.framework.dll"
  let xmldiff = &"Tools/XmlDiff/XmlDiffPatch.dll"
  let moq = &"Tools/Moq.3.1/moq.dll"
  let moqseq = &"Tools/Moq.3.1/moq.sequences.dll"
  let iTextSharp = &"ExternalLibs/iTextSharp/build/iTextSharp.dll"
  let OpenXml = &"ExternalLibs/OpenXMLSDKV2.0/DocumentFormat.OpenXml.dll"
  let qwhale = &"ExternalLibs\QwhaleEditor\Qwhale.All.dll"

////// rules
ardll "Extensibility" *> fun outname -> rule {

  let sources = fileset {
    includes "Extensibility/**/*.cs"
    includes "SL/CommonFiles/SafeGraphics.cs"
  }

  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = sources + commonSrcFiles
      References = FileList [libs.nunit]
      }
}

ardll "Diagnostics" *> fun outname -> rule {

  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = !!"Diagnostics/**/*.cs" + commonSrcFiles
      References = FileList [libs.nunit]
      }
}

ardll "Testing.Tools" *> fun outname -> rule {

  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = !! "Testing/Testing.Tools/**/*.cs" + commonSrcFiles
      References = FileList [libs.nunit; libs.xmldiff; &ardll "Extensibility"]
      }
}

ardll("Chart") *> fun outname -> rule {

  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      Define = ["ARNET"]
      SrcFiles = ls "SL/ARChart/**/*.cs" + commonSrcFiles
      References = FileList [libs.nunit]
      }
}

ardll("Document") *> fun outname -> rule {

  let src = fileset {
    includes "SL/CommonFiles/SafeGraphics.cs"
    includes "SL/DDLib.Net/Controls/**/*.cs"
    includes "SL/DDLib.Net/DDWord/kinsoku.cs"
    includes "SL/DDLib.Net/Utility/*.cs"
    includes "SL/DDLib.Net/ZLib/*.cs"
    includes "PdfExport/AR/PDFRender/BidiTable.cs"
    includes "PdfExport/AR/PDFRender/BidiReference.cs"
    includes "SL/Document/**/*.cs"
    excludes "!SL/DDLib.Net/ZLib/ZByteArray.cs"
  }

  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      Define = ["ARVIEWER_BUILD"]
      SrcFiles = src + commonSrcFiles
      References = FileList [libs.nunit] + (ardep ["Extensibility"; "Testing.Tools"])
      }
}

ardll("Core") *> fun outname -> rule {

  let src = fileset {
    includes "SL/CommonFiles/*.cs"
    includes "SL/AREngine/**/*.cs"
    includes "Reports/**/*.cs"
    includes "SL/CSS/*.cs"
    includes "SL/DDLib.Net/DDExpression/*.cs"
    includes "SL/DDLib.Net/DDWord/**/*.cs"
    includes "SL/DDLib.Net/Utility/XmlUtility.cs"
    includes "SL/DDLib.Net/Utility/GraphicsUtility.cs"
    includes "SL/DDLib.Net/Utility/DrawingUtility.cs"
    includes "SL/DDLib.Net/Core/DDFormat.cs"
    includes "SL/DDLib.Net/ZLib/*.cs"
    includes "SL/DDLib.Net/Drawing/MetaFileSaver.cs"
    includes "SL/DDLib.Net/Controls/Table/*.cs"
    includes "SL/Document/Document/LayoutUtils.cs"
    includes "SL/Document/ResourceStorage/HashCalculator.cs"

    excludes "Reports/OracleClient/**/*.cs"
    excludes "SL/CSS/AssemblyInfo.cs"
    excludes "SL/AREngine/AssemblyInfo.cs"

    join commonSrcFiles
  }

  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      Define = ["DATAMANAGER_HOST_IS_STRYKER"]
      SrcFiles = src
      References = FileList [libs.nunit] + (ardep ["Extensibility"; "Diagnostics"; "Testing.Tools"; "Document"; "Chart"])
      ReferencesGlobal = ["Microsoft.VisualBasic.dll"]
      }
}


ardll("OracleClient") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      SrcFiles = ls "Reports/OracleClient/**/*.cs" + commonSrcFiles
      References = FileList [libs.nunit; libs.moq] + (ardep ["Extensibility"; "Core"])
      ReferencesGlobal = ["System.Data.OracleClient.dll"]
      }
}


ardll("RdfExport") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      SrcFiles = ls "RDFExport/**/*.cs"
        + "Reports/ReportsCore/Rendering/CumulativeTotalsHelper.cs"
        + commonSrcFiles
      References = FileList [libs.nunit; libs.moq] + (ardep ["Extensibility"; "Core"; "Diagnostics"; "Testing.Tools"; "Document"])
      }
}


ardll("XmlExport") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      Target = Library
      OutFile = outname
      SrcFiles = fileset {
        includes "XmlExport/**/*.cs"
        includes "SL/CommonFiles/SafeGraphics.cs"
        includes "SL/Exports/*.cs"
        includes "SL/DDLib.Net/Shared/*.cs"
        includes "SL/Document/Document/LayoutUtils.cs"
        join commonSrcFiles
        }
      References = FileList [libs.nunit; libs.moq; libs.moqseq]
        + (ardep ["Extensibility"; "Core"; "Diagnostics"; "Testing.Tools"; "Document"; "RdfExport"])
      }
}


ardll("Image.Unsafe") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      Target = Library
      Unsafe = true
      OutFile = outname
      SrcFiles = ls "SL/DDLib.Net/Drawing/MonochromeBitmapTool.cs" + commonSrcFiles
      }
}

ardll("ImageExport") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = fileset {
        includes "ImageExport/**/*.cs"
        includes "Reports/ReportsCore/Rendering/Tools/Text/FontDescriptor.cs"
        includes "Reports/ReportsCore/Rendering/Tools/Cache/Services.cs"
        includes "SL/Exports/PageRangeParser.cs"
        join commonSrcFiles
        }
      References = FileList [libs.nunit; libs.moq]
        + (ardep ["Extensibility"; "Core"; "Diagnostics"; "Testing.Tools"; "Document"; "Image.Unsafe"; "RdfExport"])
      }
}


ardll("Viewer.Win") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = fileset {
        includes "UnifiedViewer/Base/Common/**/*.cs"
        includes "UnifiedViewer/Base/Properties/BaseResources.Designer.cs"
        includes "UnifiedViewer/Base/Tests/**/*.cs"
        includes "UnifiedViewer/Base/WinFormsSpecific/**/*.cs"
        includes "UnifiedViewer/WinForms/**/*.cs"
        join commonSrcFiles
        }
      References = FileList [libs.nunit; libs.moq]
        + (ardep ["Extensibility"; "Core"; "Diagnostics"; "Testing.Tools"; "Document"; "ImageExport"])
      }
}


arexe("Viewer") *> fun outname -> rule {
  do! Csc {
    CscSettings with
      OutFile = outname
      SrcFiles = ls "WinViewer/**/*.cs" + "Designer/Export/*.cs" + commonSrcFiles
      References = FileList [libs.nunit; libs.moq]
        + (ardep ["Extensibility"; "Document"; "Chart"; "Core"; "ImageExport"; "RdfExport"; "Viewer.Win"])
      }
}

// TODO Designer, Xaml, Word, Html, Excel, Dashboard, Design.Win

printfn "Building main"

let start = System.DateTime.Now

let dlls = ["Extensibility"; "Diagnostics"; "Testing.Tools"; "Chart"; "Document"; "Core"; "OracleClient"; "RdfExport"; "XmlExport"; "Image.Unsafe"; "ImageExport"; "Viewer.Win" ]
do run ((dlls |> List.map ardll) @ [arexe "Viewer"])
//do run ([arexe "Viewer"])

printfn "\nBuild completed in %A" (System.DateTime.Now - start)

