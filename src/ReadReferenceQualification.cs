using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerShapeBridge {
// Internal fixture controls, never published as general MCP read tools.
public static class ReadReferenceQualification {
 public static Dictionary<string,object> Run(string requestId,string qualificationId,string phase) {
  Guid guid;if(!Guid.TryParseExact(qualificationId,"N",out guid)||!new[]{"open_surfaces","open_curves","reopen_curves","close"}.Contains(phase))throw new ArgumentException("Invalid read qualification identity/phase.");
  Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);object application=null;
  string root=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));
  string folder=Path.Combine(root,"References_"+qualificationId);Directory.CreateDirectory(folder);
  string journal=Path.Combine(folder,requestId+".json");
  var result=new Dictionary<string,object>{{"state","rejected_before_context_change"},{"qualificationId",qualificationId},{"phase",phase},{"geometryMutationAttempted",false},{"contextChangeAttempted",false}};
  try {
   using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    application=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=application;
    if(Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("PowerShape busy.");
    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
    if(version!=config.expectedVersion)throw new InvalidOperationException("Unexpected version.");
    string ledger=Path.Combine(folder,"owned-document.json");
    if(phase=="close") {
     var owned=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(ledger,Encoding.UTF8));
     if(!Equals(owned["state"],"opened_qualification_copy"))throw new InvalidOperationException("Owned copy not open.");
     string path=(string)owned["copyPath"];
     if(!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||CheckpointTest.ModelPath(ps)!=path||Program.Hash(path)!=(string)owned["copySha256"])throw new InvalidOperationException("Owned copy context/hash differs.");
     var current=Probe.Run(false,false);if(!Equals(current["state"],"succeeded_verified"))throw new InvalidOperationException("Owned copy read failed.");
     if(Program.Json.Serialize(current["entities"])!=(string)owned["entitiesJson"])throw new InvalidOperationException("Owned copy observed properties changed; not closed.");
     result["contextChangeAttempted"]=true;TranslationTest.Journal(journal,result,"before_close");DiskVerification.CloseAudit(ps,path);
     if(Program.Hash(path)!=(string)owned["copySha256"])throw new IOException("Closed copy disk hash changed.");
     if(CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Expected baseline not restored.");
     owned["state"]="closed_qualification_copy";File.WriteAllText(ledger,Program.Json.Serialize(owned),new UTF8Encoding(false));
     result["state"]="qualified_copy_closed_baseline_restored";result["closedPath"]=path;
    } else {
     if(CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Baseline not active.");
     if(File.Exists(ledger)) {var previous=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(ledger,Encoding.UTF8));if(!Equals(previous["state"],"closed_qualification_copy"))throw new InvalidOperationException("Another owned copy remains open.");}
     bool surfaces=phase=="open_surfaces";
     string source=surfaces?Path.Combine(root,"SurfaceJob_91a4d2046f254a4a93fb9701934461b2","Fixture.psmodel"):Path.Combine(root,"BatchSections_9444de17f70346a08f12adcbfa87993f","Imported.psmodel");
     string expectedHash=surfaces?"56dd1234d09a60ff54105f7d904c1feda0390826ce81fa8847d5f283b62b02ca":"e7bc7bc164c235d8cf262c95f7f9ae2180a94cd65bcf3c248859714a84dd8819";
     if(Program.Hash(source)!=expectedHash)throw new IOException("Qualified fixture source changed.");
     string copy=Path.Combine(folder,surfaces?"SurfaceCopy.psmodel":"CurveCopy.psmodel");
     if(phase=="reopen_curves") {
      var previous=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(ledger,Encoding.UTF8));
      if(!Equals(previous["state"],"closed_qualification_copy")||!Equals(previous["copyPath"],copy)||!Equals(previous["copySha256"],expectedHash)||Program.Hash(copy)!=expectedHash)throw new InvalidOperationException("Closed curve copy identity differs.");
     } else {
      if(File.Exists(copy))throw new IOException("Copy already exists; automatic replay forbidden.");
      PlannedTranslation.CopyFrozen(source,copy);
     }
     result["copyPath"]=copy;result["sourcePath"]=source;result["sourceSha256"]=expectedHash;
     result["contextChangeAttempted"]=true;TranslationTest.Journal(journal,result,"before_open");CheckpointTest.Open(ps,copy);
     var inspection=Probe.Run(false,false);result["inspection"]=inspection;
     if(!Equals(inspection["state"],"succeeded_verified"))throw new InvalidOperationException("Opened fixture inspection failed.");
     Dictionary<string,object>[] inventory=SurfaceSectionJob.Inventory((object)ps);
     if(inventory.Length!=(surfaces?6:25)||inventory.Any(x=>Program.StrictInt(x["type"])!=(surfaces?21:59)))throw new InvalidOperationException("Fixture inventory differs.");
     var owned=new Dictionary<string,object>{{"state","opened_qualification_copy"},{"copyPath",copy},{"copySha256",Program.Hash(copy)},{"entitiesJson",Program.Json.Serialize(inspection["entities"])}};
     File.WriteAllText(ledger,Program.Json.Serialize(owned),new UTF8Encoding(false));
     if(Program.Hash(source)!=expectedHash)throw new IOException("Source fixture changed.");
     result["state"]="qualification_copy_opened";
    }
    TranslationTest.ValidateSandbox(config);result["protectedProjectHashesUnchanged"]=true;
    TranslationTest.Journal(journal,result,"completed");
   }
  }catch(Exception error){result["state"]="qualification_stopped_requires_inspection";result["error"]=error.Message;result["recoveryInstructions"]="Inspect the named copy and journal; never replay an unknown open/close or save over a source.";TranslationTest.Journal(journal,result,"stopped");}
  finally {if(application!=null&&Marshal.IsComObject(application))Marshal.ReleaseComObject(application);}
  return result;
 }
}
}
