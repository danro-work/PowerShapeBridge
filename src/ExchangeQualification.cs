using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
namespace PowerShapeBridge {
 public static class ExchangeQualification {
 public static Dictionary<string,object> CleanupFailed(string id,string failedId) {
 var r=new Dictionary<string,object>{{"recipe","fixed_failed_export_source_cleanup_v1"},{"state","rejected_before_close"},{"geometryMutationAttempted",false}};object app=null;
 try {
 Guid g;if(!Guid.TryParseExact(id,"N",out g)||!Guid.TryParseExact(failedId,"N",out g))throw new ArgumentException("Invalid cleanup identity.");
 Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"reports","exchange-"+failedId+".json"),Encoding.UTF8));
 var response=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"responses",failedId+".json"),Encoding.UTF8));
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));string target=Path.Combine(folder,"ExchangeSource_"+failedId+".psmodel");
 if(!Equals(response["state"],"timeout_outcome_unknown")||!Equals(receipt["phase"],"before_dgk_export")||!Equals(receipt["geometryMutationAttempted"],false)||!Equals(receipt["sourceCopyPath"],target)||File.Exists(Path.Combine(folder,"Exchange_"+failedId+".dgk"))||File.Exists(Path.Combine(folder,"ExchangeImported_"+failedId+".psmodel"))||Program.Hash(target)!=(string)receipt["sourceSha256"])throw new InvalidOperationException("Failed source cleanup is not qualified.");
 using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");
 int count=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER")),seen=0,found=0;if(count<0||count>256)throw new InvalidOperationException("Window count limit.");
 for(int n=1;n<=1024&&seen<count;n++) {
 if(Program.StrictInt(ps.Evaluate("WINDOW["+n+"].EXISTS"))!=1)continue;seen++;
 if(!string.Equals(Convert.ToString(ps.Evaluate("WINDOW["+n+"].TYPE")),"MODEL",StringComparison.OrdinalIgnoreCase))continue;
 string name=Convert.ToString(ps.Evaluate("WINDOW["+n+"].MODEL"));if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Invalid window model identity.");
 int model=Program.StrictInt(ps.Evaluate("MODEL['"+name+"'].ID"));string path=Convert.ToString(ps.Evaluate("MODEL[ID "+model+"].PATH"));
 if(string.IsNullOrWhiteSpace(path)||!Path.GetFullPath(path).Equals(target,StringComparison.OrdinalIgnoreCase))continue;
 if(found!=0)throw new InvalidOperationException("Ambiguous failed source window.");found=n;
 }
 if(seen!=count)throw new InvalidOperationException("Incomplete window inventory.");
 r["targetPath"]=target;r["matchingWindowCount"]=found==0?0:1;
 if(found!=0){ps.Exec("Sel_Window @"+found+" Front");if(CheckpointTest.ModelPath(ps)!=target)throw new InvalidOperationException("Cleanup active document differs.");SurfaceRecipes.Inspect(ps,21);DiskVerification.CloseAudit(ps,target);}
 TranslationTest.ValidateSandbox(c);r["state"]=found==0?"already_closed":"succeeded_test_source_closed";r["originalAndBaselineHashesUnchanged"]=true;TranslationTest.Journal(Path.Combine(Program.Root,"reports","exchange-cleanup-"+id+".json"),r,"completed");
 }
 }catch(Exception e){r["error"]=e.Message;r["state"]="cleanup_stopped_requires_inspection";}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 // Development-only verifier. It never repeats export/import and opens only a frozen saved copy.
 public static Dictionary<string,object> VerifySaved(string id,string exchangeId,string receiptDigest) {
 var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","fixed_dgk_import_saved_reopen_v1"},{"state","rejected_before_open"},{"geometryMutationAttempted",false}};
 object app=null;string journal=null;bool opened=false;
 try {
 Guid guid;if(!Guid.TryParseExact(id,"N",out guid)||!Guid.TryParseExact(exchangeId,"N",out guid))throw new ArgumentException("Invalid verification identity.");
 Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 string receiptPath=Path.Combine(Program.Root,"reports","exchange-"+exchangeId+".json");
 if(receiptDigest==null||receiptDigest.Length!=64||Program.Hash(receiptPath)!=receiptDigest)throw new InvalidOperationException("Exchange receipt changed.");
 var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receiptPath,Encoding.UTF8));
 if(!Equals(receipt["state"],"succeeded_dgk_roundtrip")||!Equals(receipt["phase"],"completed")||!Equals(receipt["qualificationId"],exchangeId))throw new InvalidOperationException("Exchange is not successful.");
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));
 string imported=Path.Combine(folder,"ExchangeImported_"+exchangeId+".psmodel"),dgk=Path.Combine(folder,"Exchange_"+exchangeId+".dgk"),copy=Path.Combine(folder,"ExchangeReopen_"+id+".psmodel");
 if(!Equals(receipt["importedProjectPath"],imported)||!Equals(receipt["dgkPath"],dgk)||Program.Hash(imported)!=(string)receipt["importedProjectSha256"]||Program.Hash(dgk)!=(string)receipt["dgkSha256"])throw new IOException("Exchange files changed.");
 using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");
 string initial=CheckpointTest.ModelPath(ps);r["activeProjectBefore"]=initial;r["processId"]=pid;r["exchangeId"]=exchangeId;r["auditCopyPath"]=copy;
 PlannedTranslation.CopyFrozen(imported,copy);journal=Path.Combine(Program.Root,"reports","exchange-reopen-"+id+".json");opened=true;TranslationTest.Journal(journal,r,"before_open_copy");CheckpointTest.Open(ps,copy);
 var geometry=SurfaceRecipes.Inspect(ps,21);r["reopenedGeometry"]=geometry;r["reopenedParameters"]=SurfaceRecipes.InspectParameters(ps,geometry);
 TranslationTest.Journal(journal,r,"before_close_copy");DiskVerification.CloseAudit(ps,copy);r["auditCopyClosed"]=true;
 if(CheckpointTest.ModelPath(ps)!=initial)throw new InvalidOperationException("Prior document not restored.");
 TranslationTest.ValidateSandbox(c);if(Program.Hash(imported)!=(string)receipt["importedProjectSha256"]||Program.Hash(dgk)!=(string)receipt["dgkSha256"]||Program.Hash(receiptPath)!=receiptDigest)throw new IOException("Exchange evidence changed.");
 r["activeProjectAfter"]=initial;r["exchangeFilesAndReceiptUnchanged"]=true;r["originalAndBaselineHashesUnchanged"]=true;r["state"]="succeeded_imported_disk_verified";
 r["verificationScope"]="Independent disk copy reopened; six fixed surface bounds/areas and centre normals verified. Full topology, arbitrary surfaces and entire parametric domains not certified.";TranslationTest.Journal(journal,r,"completed");
 }
 }catch(Exception e){r["state"]=opened?"reopen_stopped_requires_inspection":"rejected_before_open";r["error"]=e.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 public static Dictionary<string,object> Run(string id) {
 var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","fixed_surface_dgk_roundtrip_v1"},{"state","rejected_before_export"},{"geometryMutationAttempted",false}};
 object app=null;string journal=null;bool started=false;
 try {
 Guid g;if(!Guid.TryParseExact(id,"N",out g))throw new ArgumentException("Invalid ID.");Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 var receiptPath=Path.Combine(Program.Root,"operations",c.qualificationOperationId+".json");
 if(Program.Hash(receiptPath)!=c.qualificationReceiptSha256)throw new InvalidOperationException("Pinned receipt changed.");
 var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receiptPath,Encoding.UTF8));
 if(!Equals(receipt["state"],"succeeded_saved")||!Equals(receipt["recipe"],"planned_offset_test_top_surface_1mm_v1"))throw new InvalidOperationException("Only the qualified offset result is supported.");
 string source=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Operations","Result_"+c.qualificationOperationId+".psmodel"));
 if(!Equals(receipt["resultPath"],source)||Program.Hash(source)!=(string)receipt["resultSha256"])throw new InvalidOperationException("Result source changed.");
 using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));Directory.CreateDirectory(folder);
 string sourceCopy=Path.Combine(folder,"ExchangeSource_"+id+".psmodel"),imported=Path.Combine(folder,"ExchangeImported_"+id+".psmodel"),dgk=Path.Combine(folder,"Exchange_"+id+".dgk");
 if(File.Exists(sourceCopy)||File.Exists(imported)||File.Exists(dgk))throw new IOException("Output collision.");
 r["qualificationId"]=id;r["sourceOperationId"]=c.qualificationOperationId;r["sourceCopyPath"]=sourceCopy;r["importedProjectPath"]=imported;r["dgkPath"]=dgk;r["sourceSha256"]=(string)receipt["resultSha256"];
 journal=Path.Combine(Program.Root,"reports","exchange-"+id+".json");
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
 int pid=Program.StrictInt(ps.Evaluate("app.pid"));if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");r["processId"]=pid;
 if(Program.Hash(source)!=(string)receipt["resultSha256"]||PlannedTranslation.CopyFrozen(source,sourceCopy)!=(string)receipt["resultSha256"])throw new IOException("Frozen export source differs.");
 started=true;r["state"]="qualification_in_progress";TranslationTest.Journal(journal,r,"before_open_source_copy");CheckpointTest.Open(ps,sourceCopy);
 var before=SurfaceRecipes.Inspect(ps,21);r["sourceGeometry"]=before;SurfaceRecipes.InspectParameters(ps,before);
 if(Program.StrictInt(ps.Evaluate("DRAWING.NUMBER"))!=0)throw new InvalidOperationException("Drawing export is not qualified.");
 TranslationTest.Journal(journal,r,"before_dgk_export");
 ps.Exec("SELECT CLEARLIST");
 foreach(var face in (Dictionary<string,object>[])before["faces"])ps.Exec("SELECT ADD SURFACE '"+(string)face["name"]+"'");
 if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=6)throw new InvalidOperationException("Export selection differs.");
 ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("SELECT CLEARLIST");
 if(!File.Exists(dgk)||new FileInfo(dgk).Length==0||CheckpointTest.ModelPath(ps)!=sourceCopy)throw new InvalidOperationException("DGK export not verified; no automatic retry.");
 r["dgkSha256"]=Program.Hash(dgk);r["dgkBytes"]=new FileInfo(dgk).Length;
 TranslationTest.Journal(journal,r,"before_new_import_model");int windows=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");
 if(!Convert.ToBoolean(ps.IsDocumentActive())||Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=windows+1)throw new InvalidOperationException("Fresh import window not verified.");
 object reference=ps.ActiveDocument;try{dynamic doc=reference;if(Program.ArrayValues((object)doc.GetModelItems()).Length!=0)throw new InvalidOperationException("Import target is not empty.");}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 r["geometryMutationAttempted"]=true;TranslationTest.Journal(journal,r,"before_dgk_import");ps.Exec("FILE IMPORT \""+dgk+"\"");ps.Exec("DISMISS");
 var after=SurfaceRecipes.Inspect(ps,21);r["importedGeometry"]=after;r["importedParameters"]=SurfaceRecipes.InspectParameters(ps,after);
 TranslationTest.Journal(journal,r,"before_save_imported_copy");ps.Exec("FILE SAVEASPATH "+imported);
 if(!File.Exists(imported)||CheckpointTest.ModelPath(ps)!=imported)throw new InvalidOperationException("Imported save not verified.");
 SurfaceRecipes.Inspect(ps,21);r["importedProjectSha256AtSave"]=Program.Hash(imported);
 TranslationTest.Journal(journal,r,"before_close_imported_copy");DiskVerification.CloseAudit(ps,imported);r["importedWindowClosed"]=true;
 if(CheckpointTest.ModelPath(ps)!=sourceCopy)throw new InvalidOperationException("Export source copy is not active for cleanup.");DiskVerification.CloseAudit(ps,sourceCopy);r["sourceCopyWindowClosed"]=true;
 // SaveAs may finish writing after Exec returns. Pin the closed file, never the live save.
 string finalHash=Program.Hash(imported);bool stable=false;
 for(int n=0;n<12;n++){System.Threading.Thread.Sleep(250);string next=Program.Hash(imported);if(next==finalHash){stable=true;break;}finalHash=next;}
 if(!stable)throw new IOException("Saved imported project did not settle after closure.");
 r["importedProjectSha256"]=finalHash;r["savedBytesChangedBeforeClosure"]=!Equals(r["importedProjectSha256AtSave"],finalHash);
 TranslationTest.ValidateSandbox(c);if(Program.Hash(source)!=(string)receipt["resultSha256"]||Program.Hash(dgk)!=(string)r["dgkSha256"])throw new IOException("Protected exchange sources changed.");
 r["sourceHashUnchanged"]=true;r["originalAndBaselineHashesUnchanged"]=true;r["state"]="succeeded_dgk_roundtrip";r["verificationScope"]="DGK exported from an independent six-surface model and imported into a fresh empty document; side bounds/raw areas and centre normals verified, then imported copy saved and both test windows closed. Full topology and imported psmodel disk reopen not certified.";TranslationTest.Journal(journal,r,"completed");
 }
 }catch(Exception e){r["state"]=started?"exchange_stopped_requires_inspection":"rejected_before_export";r["error"]=e.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 }
}
