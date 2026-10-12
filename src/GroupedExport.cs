using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PowerShapeBridge {
// Qualified cube fixture only. Each phase uses a fresh copy and a one-shot request ID.
public static class GroupedExport {
 public class InputCurve { public string sourceOperationId; public string dgkPath; public string dgkSha256; public double zWorldMm; }
 public class Plan { public string groupId; public string sourceLevel; public InputCurve[] curves; }
 public static string FileName(string sourceLevel) {
  if(string.IsNullOrWhiteSpace(sourceLevel)||sourceLevel!=sourceLevel.Trim()||sourceLevel.Length>100)throw new ArgumentException("Invalid source level name.");
  string name=sourceLevel.Replace(':','-');
  if(name=="."||name==".."||name.EndsWith(".")||name.IndexOfAny(new[]{'<','>','\"','/','\\','|','?','*','\''})>=0||name.Any(char.IsControl))throw new ArgumentException("Unsafe DGK name.");
  string stem=name.Split('.')[0].TrimEnd().ToUpperInvariant();if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem))throw new ArgumentException("Reserved DGK name.");
  return name+".dgk";
 }
 static Dictionary<string,object>[] Inventory(dynamic ps,int expected) {
  object reference=ps.ActiveDocument;
  try {dynamic doc=reference;if(Program.StrictInt(doc.ActiveWorkplane)!=0||Convert.ToString(ps.Evaluate("unit[length].name"))!="mm")throw new InvalidOperationException("World mm required.");
   object[] items=Program.ArrayValues((object)doc.GetModelItems());if(items.Length!=expected)throw new InvalidOperationException("Group curve count differs.");if(expected==0)return new Dictionary<string,object>[0];
   object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
   var tt=Program.ArrayValues(types);var ii=Program.ArrayValues(ids);var nn=Program.ArrayValues(names);
   if(tt.Length!=expected||ii.Length!=expected||nn.Length!=expected||tt.Any(x=>Program.StrictInt(x)!=59))throw new InvalidOperationException("Only composite curves allowed in group fixture.");
   return Enumerable.Range(0,expected).Select(n=>new Dictionary<string,object>{{"nativeId",Program.StrictInt(ii[n])},{"compositeId",Program.StrictInt(items[n])},{"name",Convert.ToString(nn[n])}}).ToArray();
  }finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 }
 static void NewDocument(dynamic ps) {int n=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=n+1)throw new InvalidOperationException("New group window differs.");Inventory(ps,0);}
 static string SaveClose(dynamic ps,string path) {if(File.Exists(path))throw new IOException("Group output collision.");ps.Exec("SELECT CLEARLIST");ps.Exec("FILE SAVEASPATH "+path);if(!File.Exists(path)||CheckpointTest.ModelPath(ps)!=path)throw new IOException("Group save differs.");DiskVerification.CloseAudit(ps,path);return SliceRecipes.ClosedFileHash(path);}
 static Dictionary<string,object> Receipt(string folder,int index) {return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"Append_"+index+".json"),Encoding.UTF8));}
 static void CheckInput(InputCurve c) {
  Guid g;if(!Guid.TryParseExact(c.sourceOperationId,"N",out g)||double.IsNaN(c.zWorldMm)||double.IsInfinity(c.zWorldMm)||c.zWorldMm<0||c.zWorldMm>19.9)throw new ArgumentException("Invalid cube input.");
  string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","Slices_"+c.sourceOperationId));
  if(Path.GetDirectoryName(Path.GetFullPath(c.dgkPath))!=folder||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(c.dgkPath),"^Layer_[0-9]{4}\\.dgk$")||Program.Hash(c.dgkPath)!=c.dgkSha256)throw new IOException("Input curve path/hash differs.");
 }
 public static Dictionary<string,object> Run(string requestId,string groupId,string planHash,string phase,int index) {
  var r=new Dictionary<string,object>{{"schemaVersion",1},{"groupId",groupId},{"requestId",requestId},{"state","rejected_before_open"},{"phase",phase},{"index",index},{"sourceGeometryMutationAttempted",false}};
  object app=null;string journal=null;bool started=false;
  try {
   Guid g;if(!Guid.TryParseExact(groupId,"N",out g)||!Guid.TryParseExact(requestId,"N",out g))throw new ArgumentException("Invalid group/request identity.");
   Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);
   string planPath=Path.Combine(Program.Root,"groups",groupId+".json");if(Program.Hash(planPath)!=planHash)throw new IOException("Group plan changed.");
   var plan=Program.Json.Deserialize<Plan>(File.ReadAllText(planPath,Encoding.UTF8));string fileName=FileName(plan.sourceLevel);
   if(plan.groupId!=groupId||plan.sourceLevel!="0:General"||plan.curves==null||plan.curves.Length<1||plan.curves.Length>200||plan.curves.Select(c=>c.zWorldMm).Distinct().Count()!=plan.curves.Length)throw new ArgumentException("Only qualified cube level 0:General allowed.");
   foreach(var curve in plan.curves)CheckInput(curve);
   if(!new[]{"append","export","import","verify"}.Contains(phase)||index<0||phase=="append"&&index>=plan.curves.Length||phase=="verify"&&(index>=plan.curves.Length||index%2!=0)||phase!="append"&&phase!="verify"&&index!=0)throw new ArgumentException("Invalid bounded group phase.");
   string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","Group_"+groupId));Directory.CreateDirectory(folder);
   string stable=phase=="append"?"Append_"+index+".json":phase=="export"?"ExportReceipt.json":phase=="import"?"ImportReceipt.json":"Verify_"+index+".json";
   if(File.Exists(Path.Combine(folder,stable)))throw new IOException("Phase already completed; replay forbidden.");
   using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    using(var claim=new FileStream(Path.Combine(folder,requestId+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
    if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy PowerShape.");
    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
    if(version!=config.expectedVersion||CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Version/baseline differs.");
    r["processId"]=pid;r["planSha256"]=planHash;r["sourceLevel"]=plan.sourceLevel;
    journal=Path.Combine(folder,requestId+".json");started=true;r["state"]="group_phase_in_progress";TranslationTest.Journal(journal,r,"before_"+phase);
    if(phase=="append") {
     if(File.Exists(Path.Combine(folder,"Append_"+index+".json")))throw new IOException("Append already completed; replay forbidden.");
     if(index==0)NewDocument(ps);else {
      var previous=Receipt(folder,index-1);string path=Path.Combine(folder,"Accumulated_"+(index-1)+".psmodel");
      if(!Equals(previous["state"],"succeeded_group_phase")||Program.Hash(path)!=(string)previous["modelSha256"])throw new IOException("Prior accumulator changed.");
      string working=Path.Combine(folder,"Working_"+requestId+".psmodel");PlannedTranslation.CopyFrozen(path,working);CheckpointTest.Open(ps,working);
     }
     Dictionary<string,object>[] before=Inventory(ps,index);ps.Exec("FILE IMPORT \""+plan.curves[index].dgkPath+"\"");ps.Exec("DISMISS");
     Dictionary<string,object>[] after=Inventory(ps,index+1);var added=after.Where(x=>!before.Any(b=>Equals(b["nativeId"],x["nativeId"]))).ToArray();if(added.Length!=1)throw new InvalidOperationException("Append entity identity differs.");
     r["curve"]=SectionQualification.InspectCurve(ps,(int)added[0]["nativeId"],(int)added[0]["compositeId"],(string)added[0]["name"],plan.curves[index].zWorldMm);
     string saved=Path.Combine(folder,"Accumulated_"+index+".psmodel");r["modelPath"]=saved;r["modelSha256"]=SaveClose(ps,saved);r["curveCount"]=index+1;
    } else if(phase=="export") {
     var last=Receipt(folder,plan.curves.Length-1);string saved=(string)last["modelPath"];if(Program.Hash(saved)!=(string)last["modelSha256"])throw new IOException("Complete group model changed.");
     string working=Path.Combine(folder,"Export_"+requestId+".psmodel");PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);var inventory=Inventory(ps,plan.curves.Length);
     ps.Exec("SELECT CLEARLIST");foreach(var c in inventory){string name=(string)c["name"];if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe curve selection name.");ps.Exec("ADD COMPCURVE '"+name+"'");}
     if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=plan.curves.Length)throw new InvalidOperationException("Group export selection differs.");
     string dgk=Path.Combine(folder,fileName),probe=Path.Combine(folder,"Filename with spaces.dgk");if(File.Exists(dgk)||File.Exists(probe))throw new IOException("DGK collision.");
     ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("FILE EXPORT '"+probe+"'");ps.Exec("SELECT CLEARLIST");if(!File.Exists(dgk)||new FileInfo(dgk).Length==0||!File.Exists(probe)||new FileInfo(probe).Length==0)throw new IOException("Group exports absent.");
     r["dgkPath"]=dgk;r["dgkSha256"]=SliceRecipes.ClosedFileHash(dgk);r["spaceProbePath"]=probe;r["spaceProbeSha256"]=SliceRecipes.ClosedFileHash(probe);r["curveCount"]=inventory.Length;DiskVerification.CloseAudit(ps,working);
    } else if(phase=="import") {
     var exported=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"ExportReceipt.json"),Encoding.UTF8));
     string dgk=(string)exported["dgkPath"];if(Program.Hash(dgk)!=(string)exported["dgkSha256"]||Program.Hash((string)exported["spaceProbePath"])!=(string)exported["spaceProbeSha256"])throw new IOException("Exported group changed.");
     NewDocument(ps);ps.Exec("FILE IMPORT \""+dgk+"\"");ps.Exec("DISMISS");Dictionary<string,object>[] inventory=Inventory(ps,plan.curves.Length);
     foreach(var c in inventory)c["zWorldMm"]=Program.Vector3(ps.Evaluate("COMPCURVE[ID "+(int)c["nativeId"]+"].MIN_RANGE"))[2];
     double[] actual=inventory.Select(c=>(double)c["zWorldMm"]).OrderBy(z=>z).ToArray();double[] expected=plan.curves.Select(c=>c.zWorldMm).OrderBy(z=>z).ToArray();if(!TranslationTest.Same(actual,expected))throw new InvalidOperationException("Imported group height coverage differs.");
     r["inventory"]=inventory;r["curveCount"]=inventory.Length;string saved=Path.Combine(folder,"Imported.psmodel");r["modelPath"]=saved;r["modelSha256"]=SaveClose(ps,saved);
    } else {
     var imported=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"ImportReceipt.json"),Encoding.UTF8));string saved=(string)imported["modelPath"];if(Program.Hash(saved)!=(string)imported["modelSha256"])throw new IOException("Imported group changed.");
     string working=Path.Combine(folder,"Verify_"+requestId+".psmodel");PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);Dictionary<string,object>[] inventory=Inventory(ps,plan.curves.Length);
     var recorded=Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(imported["inventory"]));
     var readings=new List<Dictionary<string,object>>();
     // Sorted identity does not imply Z order; match each requested height by native bounds.
     foreach(double z in plan.curves.Skip(index).Take(2).Select(c=>c.zWorldMm)) {
      var candidates=recorded.Where(c=>Math.Abs(Program.StrictDouble(c["zWorldMm"])-z)<=1e-6).ToArray();if(candidates.Length!=1)throw new InvalidOperationException("Duplicate/missing group height.");
      int native=Program.StrictInt(candidates[0]["nativeId"]);var match=inventory.SingleOrDefault(c=>(int)c["nativeId"]==native&&Equals(c["name"],candidates[0]["name"]));
      if(match==null)throw new InvalidOperationException("Reopened curve identity differs.");readings.Add(SectionQualification.InspectCurve(ps,(int)match["nativeId"],(int)match["compositeId"],(string)match["name"],z));
     }
     r["verifiedCurves"]=readings.ToArray();DiskVerification.CloseAudit(ps,working);
    }
    TranslationTest.ValidateSandbox(config);foreach(var curve in plan.curves)CheckInput(curve);if(CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Baseline context differs after phase.");
    r["testWindowClosed"]=true;r["protectedSourceHashesUnchanged"]=true;r["state"]="succeeded_group_phase";TranslationTest.Journal(journal,r,"completed");
    if(File.Exists(Path.Combine(folder,stable)))throw new IOException("Phase receipt collision.");PlannedTranslation.SaveJson(Path.Combine(folder,stable),r);
   }
  }catch(Exception e){r["state"]=started?"group_outcome_unknown_requires_inspection":"rejected_before_open";r["error"]=e.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
  finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
  return r;
 }
}
}
