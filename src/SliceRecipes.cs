using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PowerShapeBridge {
 public static class SliceRecipes {
 public static double[] Parse(Dictionary<string,object> args) {
 if(args.Count!=3||!args.ContainsKey("firstZMm")||!args.ContainsKey("lastZMm")||!args.ContainsKey("stepMm"))throw new ArgumentException("Only firstZMm, lastZMm, stepMm accepted.");
 var p=new[]{Program.StrictDouble(args["firstZMm"]),Program.StrictDouble(args["lastZMm"]),Program.StrictDouble(args["stepMm"])};Heights(p);return p;
 }
 public static double[] Heights(double[] p) {
 if(p==null||p.Length!=3||p.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>20||Math.Round(x,6)!=x))throw new ArgumentException("Slice values must be finite mm with at most six decimal places.");
 long first=(long)Math.Round(p[0]*1000000),last=(long)Math.Round(p[1]*1000000),step=(long)Math.Round(p[2]*1000000);
 if(first<0||first>0&&first<100000||last>19900000||last<first||step<100000||(last-first)%step!=0)throw new ArgumentException("Qualified first height is zero or >=0.1 mm; last<=19.9; step>=0.1 mm; last must lie on the exact step grid.");
 long count=1+(last-first)/step;if(count>3)throw new ArgumentException("Only 1..3 slices per qualified operation.");
 return Enumerable.Range(0,(int)count).Select(n=>(first+n*step)/1000000.0).ToArray();
 }
 internal static Dictionary<string,object>[] InspectMany(dynamic ps,double[] heights,bool withSolid) {
 object reference=ps.ActiveDocument;
 try {
 dynamic doc=reference;if(Program.StrictInt(doc.ActiveWorkplane)!=0||Convert.ToString(ps.Evaluate("unit[length].name"))!="mm")throw new InvalidOperationException("World mm required.");
 object[] items=Program.ArrayValues((object)doc.GetModelItems());object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
 var tt=Program.ArrayValues(types);var ii=Program.ArrayValues(ids);var nn=Program.ArrayValues(names);
 int count=heights.Length+(withSolid?1:0);
 if(items.Length!=count||tt.Length!=count||ii.Length!=count||nn.Length!=count||tt.Count(x=>Program.StrictInt(x)==59)!=heights.Length||tt.Count(x=>Program.StrictInt(x)==40)!=(withSolid?1:0))throw new InvalidOperationException("Slice inventory differs.");
 var result=new Dictionary<string,object>[heights.Length];
 for(int n=0;n<count;n++) {
 if(Program.StrictInt(tt[n])!=59)continue;int native=Program.StrictInt(ii[n]);double[] min=Program.Vector3(ps.Evaluate("COMPCURVE[ID "+native+"].MIN_RANGE"));
 int index=Array.FindIndex(heights,z=>Math.Abs(z-min[2])<=1e-6);if(index<0||result[index]!=null)throw new InvalidOperationException("Missing, duplicate or unexpected slice height.");
 result[index]=SectionQualification.InspectCurve(ps,native,Program.StrictInt(items[n]),Convert.ToString(nn[n]),heights[index]);result[index]["layerIndex"]=index+1;
 }
 if(result.Any(x=>x==null))throw new InvalidOperationException("Incomplete layer coverage.");return result;
 }finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 }
 internal static string ClosedFileHash(string path) {
 string hash=Program.Hash(path);int same=0;
 for(int n=0;n<16;n++){System.Threading.Thread.Sleep(250);string next=Program.Hash(path);same=next==hash?same+1:0;hash=next;if(same>=4)return hash;}
 throw new IOException("Closed model file did not settle; no automatic retry.");
 }
 public static Dictionary<string,object> VerifyExisting(string id,string sourceId,string sourceDigest) {
 var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","existing_slice_batch_verification_v1"},{"state","rejected_before_open"},{"originalGeometryMutationAttempted",false}};object app=null;string journal=null;
 try {
 Guid g;if(!Guid.TryParseExact(id,"N",out g)||!Guid.TryParseExact(sourceId,"N",out g))throw new ArgumentException("Invalid verification identity.");Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 string sourceReceipt=Path.Combine(Program.Root,"operations",sourceId+".json");if(Program.Hash(sourceReceipt)!=sourceDigest)throw new InvalidOperationException("Source receipt changed.");
 var source=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(sourceReceipt,Encoding.UTF8));
 if(!Equals(source["operationId"],sourceId)||!Equals(source["recipe"],"planned_slice_test_cube_xy_v1")||!Equals(source["resultWindowClosed"],true)||!Equals(source["resultAuditClosed"],true))throw new InvalidOperationException("Saved/reopened source batch is not available.");
 var plan=Program.Json.Deserialize<PlannedTranslation.Plan>(Program.Json.Serialize(source["plan"]));double[] heights=Heights(plan.sliceParametersMm);
 if(plan.operationId!=sourceId||plan.sliceHeightsWorldMm==null||!heights.SequenceEqual(plan.sliceHeightsWorldMm))throw new InvalidOperationException("Source grid differs.");
 string sourceFolder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","Slices_"+sourceId)),saved=Path.Combine(sourceFolder,"Result.psmodel"),checkpoint=Path.Combine(sourceFolder,"Checkpoint.psmodel");
 if(!Equals(source["resultPath"],saved)||!Equals(source["checkpointPath"],checkpoint)||Program.Hash(saved)!=(string)source["resultSha256"]||Program.Hash(checkpoint)!=(string)source["checkpointSha256"])throw new IOException("Existing batch model hashes differ.");
 var layers=Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(source["layers"]));if(layers.Length!=heights.Length)throw new InvalidOperationException("Source layer count differs.");
 for(int n=0;n<layers.Length;n++){string dgk=Path.Combine(sourceFolder,"Layer_"+(n+1).ToString("D4",CultureInfo.InvariantCulture)+".dgk");if(Program.StrictInt(layers[n]["layerIndex"])!=n+1||Program.StrictDouble(layers[n]["zWorldMm"])!=heights[n]||!Equals(layers[n]["dgkPath"],dgk)||Program.Hash(dgk)!=(string)layers[n]["dgkSha256"])throw new IOException("Existing layer identity/hash differs.");}
 using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(pid!=Program.StrictInt(source["processId"])||!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy source instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","SliceVerification_"+id));if(Directory.Exists(folder))throw new IOException("Verification output collision.");Directory.CreateDirectory(folder);
 r["verificationId"]=id;r["sourceOperationId"]=sourceId;r["sourceReceiptSha256"]=sourceDigest;r["sourceResultSha256"]=source["resultSha256"];r["sourceCheckpointSha256"]=source["checkpointSha256"];r["plan"]=plan;r["layers"]=layers;r["state"]="verification_in_progress";
 journal=Path.Combine(Program.Root,"reports","slice-verification-"+id+".json");string audit=Path.Combine(folder,"ResultAudit.psmodel");PlannedTranslation.CopyFrozen(saved,audit);TranslationTest.Journal(journal,r,"before_open_existing_result");CheckpointTest.Open(ps,audit);r["reopenedCurves"]=InspectMany(ps,heights,true);
 var before=Program.Json.Deserialize<CheckpointTest.Measurement>(Program.Json.Serialize(source["before"]));if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps,false,false,heights.Length)))throw new InvalidOperationException("Existing source solid differs.");DiskVerification.CloseAudit(ps,audit);r["resultAuditClosed"]=true;
 for(int n=0;n<layers.Length;n++) {
 // A fresh verification document, never a repeated slicing operation or an edit to an uncertain document.
 TranslationTest.Journal(journal,r,"before_fresh_layer_import");int count=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=count+1)throw new InvalidOperationException("Verification import window differs.");
 object reference=ps.ActiveDocument;try{dynamic doc=reference;if(Program.ArrayValues((object)doc.GetModelItems()).Length!=0)throw new InvalidOperationException("Verification target not empty.");}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 ps.Exec("FILE IMPORT \""+(string)layers[n]["dgkPath"]+"\"");ps.Exec("DISMISS");var reading=InspectMany(ps,new[]{heights[n]},false)[0];reading["layerIndexInImportDocument"]=1;reading["layerIndex"]=n+1;layers[n]["verificationImportedCurve"]=reading;
 string imported=Path.Combine(folder,"VerifiedLayer_"+(n+1).ToString("D4",CultureInfo.InvariantCulture)+".psmodel");ps.Exec("FILE SAVEASPATH "+imported);if(!File.Exists(imported)||CheckpointTest.ModelPath(ps)!=imported)throw new InvalidOperationException("Verification save differs.");DiskVerification.CloseAudit(ps,imported);layers[n]["verificationImportedProjectPath"]=imported;layers[n]["verificationImportedProjectSha256"]=ClosedFileHash(imported);layers[n]["verificationWindowClosed"]=true;
 }
 TranslationTest.ValidateSandbox(c);if(Program.Hash(sourceReceipt)!=sourceDigest||Program.Hash(saved)!=(string)source["resultSha256"]||Program.Hash(checkpoint)!=(string)source["checkpointSha256"])throw new IOException("Existing batch evidence changed.");
 foreach(var layer in layers)if(Program.Hash((string)layer["dgkPath"])!=(string)layer["dgkSha256"])throw new IOException("Existing DGK changed.");
 r["state"]="succeeded_existing_slice_batch_verified";r["originalAndBaselineHashesUnchanged"]=true;r["qualification"]="Existing combined result and all existing DGKs verified on new independent copies/documents. Source operation journal preserved, no slicing command repeated; initial uncertain document left untouched.";TranslationTest.Journal(journal,r,"completed");
 }
 }catch(Exception e){r["state"]="verification_stopped_requires_inspection";r["error"]=e.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 public static Dictionary<string,object> Execute(string id,string digest) {
 var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","planned_slice_test_cube_xy_v1"},{"state","rejected_before_mutation"},{"mutationAttempted",false}};
 object app=null;string journal=null;bool attempted=false;
 try {
 Guid g;if(!Guid.TryParseExact(id,"N",out g))throw new ArgumentException("Invalid operation identity.");Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 string operations=Path.Combine(Program.Root,"operations"),planPath=Path.Combine(Program.Root,"plans",id+".json");string planText=File.ReadAllText(planPath,Encoding.UTF8);
 if(planText.Length>65536||PlannedTranslation.Digest(planText)!=digest)throw new InvalidOperationException("Reviewed plan changed.");
 var plan=Program.Json.Deserialize<PlannedTranslation.Plan>(planText);if(plan.operationId!=id||plan.recipeId!="slice_test_cube_xy")throw new InvalidOperationException("Recipe identity differs.");
 using(var lane=new FileStream(Path.Combine(operations,"executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 if(File.Exists(Path.Combine(operations,id+".once")))throw new InvalidOperationException("Operation ID already claimed; replay forbidden.");
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");
 string path=CheckpointTest.ModelPath(ps);if(path!=Path.GetFullPath(c.expectedProject)||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Active baseline and empty selection required.");
 CheckpointTest.Measurement before=CheckpointTest.Measure(ps);
 PlannedTranslation.ValidatePreparedPlan(plan,pid,path,PlannedTranslation.Fingerprint(pid,path,before),PlannedTranslation.Geometry(before),DateTime.UtcNow);
 if(plan.beforeLevelNumber!=PlannedTranslation.SolidLevel(ps,before.nativeId)||!TranslationTest.Same(before.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0})||!TranslationTest.Same(plan.expectedBoundsWorldMm,before.boundsWorldMm)||!TranslationTest.Same(plan.expectedCogWorldMm,before.cogWorldMm))throw new InvalidOperationException("Source solid or expected properties changed.");
 double[] heights=plan.sliceHeightsWorldMm;
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","Slices_"+id));if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Slice output folder collision.");
 using(var gate=new FileStream(Path.Combine(operations,id+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=Encoding.UTF8.GetBytes(digest);gate.Write(bytes,0,bytes.Length);gate.Flush(true);}
 Directory.CreateDirectory(folder);string checkpoint=Path.Combine(folder,"Checkpoint.psmodel"),working=Path.Combine(folder,"Working.psmodel"),saved=Path.Combine(folder,"Result.psmodel"),audit=Path.Combine(folder,"ResultAudit.psmodel");
 r["operationId"]=id;r["plan"]=plan;r["before"]=before;r["processId"]=pid;r["checkpointPath"]=checkpoint;r["workingPath"]=working;r["resultPath"]=saved;r["outputDirectory"]=folder;
 journal=Path.Combine(operations,id+".json");r["state"]="execution_in_progress";TranslationTest.Journal(journal,r,"before_copy_source");
 r["checkpointSha256"]=PlannedTranslation.CopyFrozen(path,checkpoint);PlannedTranslation.CopyFrozen(checkpoint,working);
 TranslationTest.Journal(journal,r,"before_open_working");CheckpointTest.Open(ps,working);
 if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps)))throw new InvalidOperationException("Working copy differs.");
 string name=Convert.ToString(ps.Evaluate("SOLID[ID "+before.nativeId+"].NAME"));if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe solid name.");
 ps.Exec("PRINCIPALPLANE XY");
 for(int n=0;n<heights.Length;n++) {
 ps.Exec("SELECT CLEARLIST ADD SOLID '"+name+"'");if(CheckpointTest.ModelPath(ps)!=working||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1)throw new InvalidOperationException("Slice selection differs.");
 attempted=true;r["mutationAttempted"]=true;r["creatingLayerIndex"]=n+1;TranslationTest.Journal(journal,r,"before_create_slice");
 ps.Exec("CREATE CURVE OBLIQUE");ps.Exec("DISTANCE "+heights[n].ToString("0.######",CultureInfo.InvariantCulture));ps.Exec("ACCEPT");ps.Exec("SELECT CLEARLIST");
 InspectMany(ps,heights.Take(n+1).ToArray(),true);
 }
 r["curves"]=InspectMany(ps,heights,true);if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps,false,false,heights.Length)))throw new InvalidOperationException("Solid changed during slicing.");
 var layers=new List<Dictionary<string,object>>();r["layers"]=layers;
 foreach(var curve in (Dictionary<string,object>[])r["curves"]) {
 int index=(int)curve["layerIndex"];string curveName=(string)curve["name"];if(string.IsNullOrWhiteSpace(curveName)||curveName.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe curve identity.");
 string dgk=Path.Combine(folder,"Layer_"+index.ToString("D4",CultureInfo.InvariantCulture)+".dgk");
 var layer=new Dictionary<string,object>{{"layerIndex",index},{"zWorldMm",heights[index-1]},{"dgkPath",dgk},{"curve",curve}};layers.Add(layer);TranslationTest.Journal(journal,r,"before_export_layer");
 ps.Exec("SELECT CLEARLIST ADD COMPCURVE '"+curveName+"'");if(CheckpointTest.ModelPath(ps)!=working||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1)throw new InvalidOperationException("Layer export selection differs.");
 ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("SELECT CLEARLIST");if(!File.Exists(dgk)||new FileInfo(dgk).Length==0)throw new IOException("Layer DGK absent.");layer["dgkSha256"]=Program.Hash(dgk);layer["dgkBytes"]=new FileInfo(dgk).Length;
 }
 TranslationTest.Journal(journal,r,"before_save_result");ps.Exec("FILE SAVEASPATH "+saved);if(!File.Exists(saved)||CheckpointTest.ModelPath(ps)!=saved)throw new InvalidOperationException("Slice save differs.");
 DiskVerification.CloseAudit(ps,saved);r["resultWindowClosed"]=true;r["resultSha256"]=ClosedFileHash(saved);
 PlannedTranslation.CopyFrozen(saved,audit);TranslationTest.Journal(journal,r,"before_reopen_result");CheckpointTest.Open(ps,audit);r["reopenedCurves"]=InspectMany(ps,heights,true);
 if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps,false,false,heights.Length)))throw new InvalidOperationException("Saved source solid differs.");DiskVerification.CloseAudit(ps,audit);r["resultAuditClosed"]=true;
 foreach(var layer in layers) {
 TranslationTest.Journal(journal,r,"before_import_layer");int windows=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=windows+1)throw new InvalidOperationException("New import window differs.");
 object reference=ps.ActiveDocument;try{dynamic doc=reference;if(Program.ArrayValues((object)doc.GetModelItems()).Length!=0)throw new InvalidOperationException("Layer import target not empty.");}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 ps.Exec("FILE IMPORT \""+(string)layer["dgkPath"]+"\"");ps.Exec("DISMISS");Dictionary<string,object> importedCurve=InspectMany(ps,new[]{(double)layer["zWorldMm"]},false)[0];importedCurve["layerIndexInImportDocument"]=1;importedCurve["layerIndex"]=(int)layer["layerIndex"];layer["importedCurve"]=importedCurve;
 string imported=Path.Combine(folder,"Layer_"+((int)layer["layerIndex"]).ToString("D4",CultureInfo.InvariantCulture)+"_Imported.psmodel");layer["importedProjectPath"]=imported;
 ps.Exec("FILE SAVEASPATH "+imported);if(!File.Exists(imported)||CheckpointTest.ModelPath(ps)!=imported)throw new InvalidOperationException("Layer import save differs.");DiskVerification.CloseAudit(ps,imported);layer["importedWindowClosed"]=true;layer["importedProjectSha256"]=ClosedFileHash(imported);
 }
 TranslationTest.ValidateSandbox(c);if(Program.Hash(saved)!=(string)r["resultSha256"]||Program.Hash(checkpoint)!=(string)r["checkpointSha256"]||CheckpointTest.ModelPath(ps)!=path)throw new InvalidOperationException("Slice protected sources or active context changed.");
 foreach(var layer in layers)if(Program.Hash((string)layer["dgkPath"])!=(string)layer["dgkSha256"]||Program.Hash((string)layer["importedProjectPath"])!=(string)layer["importedProjectSha256"])throw new IOException("Layer evidence changed.");
 r["originalAndBaselineHashesUnchanged"]=true;r["verificationScope"]="1..3 interior square slices, all layer DGKs reimported individually, combined solid/curves result reopened from disk. Imported layer psmodels not separately reopened; arbitrary slice topology not certified. Principal plane XY remains.";
 r["state"]="succeeded_slices_saved_reopened_dgk_verified";r["phase"]="completed";r["manifestPath"]=Path.Combine(folder,"manifest.json");PlannedTranslation.SaveJson((string)r["manifestPath"],r);TranslationTest.Journal(journal,r,"completed");
 }
 }catch(Exception e){r["state"]=attempted?"outcome_unknown_requires_manual_check":"rejected_before_mutation";r["error"]=e.Message;r["recoveryInstructions"]="Inspect the journal and detached checkpoint; do not retry this operation ID.";if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 }
}
