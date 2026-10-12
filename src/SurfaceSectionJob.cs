using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PowerShapeBridge {
// Select surfaces by type AND source level, never all entities of a level.
public static class SurfaceSectionJob {
 public class Plan { public string jobId; public string sourceReceiptSha256; public double[] heightsWorldMm; }
 internal static Dictionary<string,object>[] Inventory(dynamic ps,bool requireWorld=true) {
  object reference=ps.ActiveDocument;try {dynamic doc=reference;int workplane=Program.StrictInt(doc.ActiveWorkplane);string units=Convert.ToString(ps.Evaluate("unit[length].name"));if(requireWorld&&(workplane!=0||units!="mm"))throw new InvalidOperationException("World mm required: workplane="+workplane+", lengthUnit="+units);
   object[] items=Program.ArrayValues((object)doc.GetModelItems());if(items.Length==0)return new Dictionary<string,object>[0];object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
   var tt=Program.ArrayValues(types);var ii=Program.ArrayValues(ids);var nn=Program.ArrayValues(names);var ll=Program.ArrayValues(levels);if(new[]{tt,ii,nn,ll}.Any(a=>a.Length!=items.Length))throw new InvalidOperationException("Incomplete surface/curve inventory.");
   return Enumerable.Range(0,items.Length).Select(n=>new Dictionary<string,object>{{"type",Program.StrictInt(tt[n])},{"nativeId",Program.StrictInt(ii[n])},{"compositeId",Program.StrictInt(items[n])},{"name",Convert.ToString(nn[n])},{"levelNumber",Program.StrictInt(ll[n])}}).ToArray();
  }finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 }
 public static Dictionary<string,object> InspectRecovery(string jobId,string sourceRequestId) {
  Guid g;if(!Guid.TryParseExact(jobId,"N",out g)||!Guid.TryParseExact(sourceRequestId,"N",out g))throw new ArgumentException("Invalid recovery inspection identity.");Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);object app=null,reference=null;
  try{app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;string expected=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","SurfaceJob_"+jobId,"Working_"+sourceRequestId+".psmodel"));if(CheckpointTest.ModelPath(ps)!=expected)throw new InvalidOperationException("Recovery inspection document differs.");reference=ps.ActiveDocument;dynamic doc=reference;
   return new Dictionary<string,object>{{"state","inspected_surface_recovery"},{"modelPath",expected},{"workplane",Program.StrictInt(doc.ActiveWorkplane)},{"lengthUnit",Convert.ToString(ps.Evaluate("unit[length].name"))},{"selectionCount",Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))},{"inventory",Inventory(ps,false)}};
  }finally{if(reference!=null&&Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 }
 internal static void ValidateSurfaceSelection(Dictionary<string,object>[] inventory,int expectedCurves) {
  if(inventory.Count(c=>(int)c["type"]==21&&(int)c["levelNumber"]==1)!=4||inventory.Count(c=>(int)c["type"]==21&&(int)c["levelNumber"]==2)!=2||inventory.Count(c=>(int)c["type"]==59&&(int)c["levelNumber"]==101)!=expectedCurves||inventory.Length!=6+expectedCurves)throw new InvalidOperationException("Working/auxiliary/contour level inventory differs.");
 }
 public static Dictionary<string,object>[] WorkingSurfaces(Dictionary<string,object>[] inventory,int level) {return inventory.Where(c=>Program.StrictInt(c["type"])==21&&Program.StrictInt(c["levelNumber"])==level).ToArray();}
 static void SettleInventory(dynamic ps,int expectedCurves) {
  Exception last=null;for(int attempt=0;attempt<30;attempt++){try{ValidateSurfaceSelection(Inventory(ps),expectedCurves);return;}catch(InvalidOperationException e){last=e;}catch(FormatException e){last=e;}System.Threading.Thread.Sleep(100);}throw new InvalidOperationException("Readback did not settle after level update: "+last.Message);
 }
 static string Name(Dictionary<string,object> entity) {string name=(string)entity["name"];if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe selected identity.");return name;}
 internal static int ReadLevelState(object application,int level) {
  dynamic ps=application;for(int n=0;n<30;n++){try{return Program.StrictInt(ps.Evaluate("LEVEL["+level+"].ACTIVE"));}catch(FormatException){System.Threading.Thread.Sleep(100);}}throw new InvalidOperationException("Level state read did not settle.");
 }
 internal static void Select(dynamic ps,Dictionary<string,object>[] entities,string type) {
  foreach(int level in entities.Select(c=>Program.StrictInt(c["levelNumber"])).Distinct())if(ReadLevelState((object)ps,level)!=1){ps.Exec("TOOLBAR LEVEL ON "+level);bool ready=false;for(int n=0;n<20;n++){if(ReadLevelState((object)ps,level)==1){ready=true;break;}System.Threading.Thread.Sleep(100);}if(!ready)throw new InvalidOperationException("Selection level did not activate.");}
  ps.Exec("SELECT CLEARLIST");foreach(var c in entities)ps.Exec("ADD "+type+" '"+Name(c)+"'");if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=entities.Length)throw new InvalidOperationException("Exact selection count differs.");
 }
 internal static Dictionary<string,object> ReadCurve(dynamic ps,Dictionary<string,object> c) {
  string prefix="COMPCURVE[ID "+(int)c["nativeId"]+"]";var r=new Dictionary<string,object>(c);
  double[] min=Program.Vector3(ps.Evaluate(prefix+".MIN_RANGE")),max=Program.Vector3(ps.Evaluate(prefix+".MAX_RANGE"));r["boundsWorldMm"]=new[]{min[0],max[0],min[1],max[1],min[2],max[2]};
  r["lengthMm"]=Program.StrictDouble(ps.Evaluate(prefix+".LENGTH"));r["areaRaw"]=Program.StrictDouble(ps.Evaluate(prefix+".AREA"));r["closed"]=Program.StrictInt(ps.Evaluate(prefix+".CLOSED"))==1;
  int count=Program.StrictInt(ps.Evaluate(prefix+".POINT.NUMBER"));if(count<2||count>16)throw new InvalidOperationException("Unexpected square curve point count.");
  r["keypointsWorldMm"]=Enumerable.Range(1,count).Select(n=>(double[])Program.Vector3(ps.Evaluate(prefix+".POINT["+n+"]"))).ToArray();return r;
 }
 public static Dictionary<string,object> ValidateSquare(Dictionary<string,object>[] curves,double z) {
  if(curves.Length<1||curves.Length>4)throw new InvalidOperationException("Square section must contain 1..4 composite curves.");
  var edges=new HashSet<string>();double total=0;
  foreach(var c in curves) {
   double[] bounds=Program.Json.Deserialize<double[]>(Program.Json.Serialize(c["boundsWorldMm"]));double length=Program.StrictDouble(c["lengthMm"]);if(bounds.Length!=6||double.IsNaN(length)||double.IsInfinity(length)||length<=0||bounds[0]<-10-1e-6||bounds[1]>10+1e-6||bounds[2]<-10-1e-6||bounds[3]>10+1e-6||Math.Abs(bounds[4]-z)>1e-6||Math.Abs(bounds[5]-z)>1e-6)throw new InvalidOperationException("Square curve range differs.");
   double[][] points=Program.Json.Deserialize<double[][]>(Program.Json.Serialize(c["keypointsWorldMm"]));var corners=new List<int>();
   foreach(var p in points){if(p.Length!=3||Math.Abs(Math.Abs(p[0])-10)>1e-6||Math.Abs(Math.Abs(p[1])-10)>1e-6||Math.Abs(p[2]-z)>1e-6)throw new InvalidOperationException("Square corner differs.");int corner=(p[0]>0?1:0)+(p[1]>0?2:0);if(corners.Count==0||corners.Last()!=corner)corners.Add(corner);}
   if((bool)c["closed"]&&corners.Last()!=corners.First())corners.Add(corners.First());int segments=0;
   for(int n=1;n<corners.Count;n++){int a=corners[n-1],b=corners[n];if((a^b)!=1&&(a^b)!=2)throw new InvalidOperationException("Unexpected diagonal section edge.");string edge=Math.Min(a,b)+"-"+Math.Max(a,b);if(!edges.Add(edge))throw new InvalidOperationException("Duplicate section edge.");segments++;}
   if(Math.Abs(length-segments*20)>1e-6)throw new InvalidOperationException("Section curve length differs from perimeter edges.");total+=length;
  }
  if(edges.Count!=4||Math.Abs(total-80)>1e-6)throw new InvalidOperationException("Incomplete square perimeter.");
  return new Dictionary<string,object>{{"zWorldMm",z},{"curveCount",curves.Length},{"totalLengthMm",total},{"uniquePerimeterEdges",edges.OrderBy(x=>x).ToArray()},{"curves",curves},{"scope","Qualified square perimeter coverage, bounds, keypoints and length; arbitrary spline topology not certified."}};
 }
 internal static string SaveClose(dynamic ps,string path) {if(File.Exists(path))throw new IOException("Output collision.");ps.Exec("SELECT CLEARLIST");ps.Exec("FILE SAVEASPATH "+path);if(!File.Exists(path)||CheckpointTest.ModelPath(ps)!=path)throw new IOException("Surface job save differs.");DiskVerification.CloseAudit(ps,path);return SliceRecipes.ClosedFileHash(path);}
 static Dictionary<string,object> ReadReceipt(string folder,string name) {var r=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,name+".json"),Encoding.UTF8));if(!Equals(r["state"],"succeeded_surface_section_phase"))throw new IOException("Incomplete prior surface phase.");return r;}
 public static Dictionary<string,object> Run(string requestId,string jobId,string planHash,string phase,int index) {
  object app=null;string journal=null;bool started=false;var r=new Dictionary<string,object>{{"schemaVersion",1},{"jobId",jobId},{"requestId",requestId},{"state","rejected_before_open"},{"index",index},{"requestedPhase",phase},{"sourceGeometryMutationAttempted",false}};
  try {
   Guid g;if(!Guid.TryParseExact(jobId,"N",out g)||!Guid.TryParseExact(requestId,"N",out g))throw new ArgumentException("Invalid surface job identity.");Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);
   string planPath=Path.Combine(Program.Root,"surfacejobs",jobId+".json");if(Program.Hash(planPath)!=planHash)throw new IOException("Surface plan changed.");var plan=Program.Json.Deserialize<Plan>(File.ReadAllText(planPath,Encoding.UTF8));
   double[] expected=Enumerable.Range(0,25).Select(n=>n*0.8).ToArray();if(plan.jobId!=jobId||plan.heightsWorldMm==null||!TranslationTest.Same(plan.heightsWorldMm,expected))throw new ArgumentException("Only qualified 25-plane cube surface fixture accepted.");
   string sourceId="ce7c3a8f6fb8410c822ebee8552c780a",sourceReceipt=Path.Combine(Program.Root,"reports","exchange-"+sourceId+".json");if(Program.Hash(sourceReceipt)!=plan.sourceReceiptSha256)throw new IOException("Qualified source receipt changed.");
   var source=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(sourceReceipt,Encoding.UTF8));string sourcePath=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","ExchangeImported_"+sourceId+".psmodel"));if(!Equals(source["state"],"succeeded_dgk_roundtrip")||!Equals(source["importedProjectPath"],sourcePath)||Program.Hash(sourcePath)!=(string)source["importedProjectSha256"])throw new IOException("Qualified six-surface source changed.");
   string recoveryId=null;if(phase.StartsWith("recover_slice_")||phase.StartsWith("recover_export_")){bool exporting=phase.StartsWith("recover_export_");recoveryId=phase.Substring(exporting?15:14);if(!Guid.TryParseExact(recoveryId,"N",out g))throw new ArgumentException("Invalid explicit recovery identity.");phase=exporting?"recover_export":"recover_slice";}
   if(!new[]{"setup","slice","recover_slice","recover_export","export","import","verify","verify_surfaces"}.Contains(phase)||index<0||index>24||phase!="slice"&&phase!="recover_slice"&&phase!="verify"&&index!=0)throw new ArgumentException("Invalid surface phase.");
   string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","SurfaceJob_"+jobId));Directory.CreateDirectory(folder);string stable=phase=="slice"||phase=="recover_slice"?"Slice_"+index:phase=="verify"?"Verify_"+index:phase=="recover_export"?"export":phase;
   if(File.Exists(Path.Combine(folder,stable+".json")))throw new IOException("Surface phase already complete; replay forbidden.");
   using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    using(var claim=new FileStream(Path.Combine(folder,requestId+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
    if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy PowerShape.");
    string initialPath=recoveryId!=null?Path.Combine(folder,"Working_"+recoveryId+".psmodel"):Path.GetFullPath(config.expectedProject);
    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=config.expectedVersion||CheckpointTest.ModelPath(ps)!=initialPath)throw new InvalidOperationException("Version/baseline or explicit recovery path differs.");
    started=true;journal=Path.Combine(folder,requestId+".json");r["state"]="surface_phase_in_progress";r["processId"]=pid;r["planSha256"]=planHash;r["sourceReceiptSha256"]=plan.sourceReceiptSha256;r["sourcePath"]=sourcePath;r["sourceSha256"]=source["importedProjectSha256"];TranslationTest.Journal(journal,r,"before_"+phase);
    string working=Path.Combine(folder,"Working_"+requestId+".psmodel");
    if(phase=="setup") {
     PlannedTranslation.CopyFrozen(sourcePath,working);CheckpointTest.Open(ps,working);Dictionary<string,object> surfaces=SurfaceRecipes.Inspect(ps,21);r["sourceSurfaces"]=surfaces;
     object reference=ps.ActiveDocument;try{dynamic doc=reference;doc.SetLevelName(1,"Vneshnie 0.8");doc.SetLevelName(2,"AuxTestCaps");doc.SetLevelName(101,"1k:Vneshnie 0.8");
      foreach(var face in (Dictionary<string,object>[])surfaces["faces"]){int level=(int)face["side"]<4?1:2;if(!Convert.ToBoolean(doc.SetItemsLevel((int)face["compositeId"],level)))throw new InvalidOperationException("Surface level assignment failed.");}
     }finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
     SettleInventory(ps,0);if(Convert.ToString(ps.Evaluate("LEVEL[1].NAME"))!="Vneshnie 0.8"||Convert.ToString(ps.Evaluate("LEVEL[101].NAME"))!="1k:Vneshnie 0.8")throw new InvalidOperationException("Working/contour names differ.");
     r["sourceLevel"]="1:Vneshnie 0.8";r["workingSurfaceLevelNumber"]=1;r["auxiliaryLevelNumber"]=2;r["contourLevelNumber"]=101;r["contourLevelName"]="1k:Vneshnie 0.8";
     string saved=Path.Combine(folder,"Fixture.psmodel");r["modelPath"]=saved;r["modelSha256"]=SaveClose(ps,saved);
    } else if(phase=="slice"||phase=="recover_slice") {
     var previous=ReadReceipt(folder,index==0?"setup":"Slice_"+(index-1));string saved=(string)previous["modelPath"];if(Program.Hash(saved)!=(string)previous["modelSha256"])throw new IOException("Prior surface model changed.");
     Dictionary<string,object>[] before=null,added=null;int curveCount=0;
     if(phase=="recover_slice") {
      string failedPath=Path.Combine(folder,recoveryId+".json");var failed=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(failedPath,Encoding.UTF8));
      if(!Equals(failed["state"],"surface_outcome_unknown_requires_inspection")||!Equals(failed["jobId"],jobId)||!Equals(failed["requestedPhase"],"slice")||Program.StrictInt(failed["index"])!=index||!Equals(failed["planSha256"],planHash)||Program.Hash(initialPath)!=(string)previous["modelSha256"])throw new InvalidOperationException("Explicit recovery evidence differs.");
      var created=Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(failed["createdInventory"]));Dictionary<string,object>[] inventory=Inventory(ps);added=inventory.Where(c=>created.Any(x=>Program.StrictInt(x["compositeId"])==(int)c["compositeId"])).ToArray();
      if(added.Length!=created.Length||added.Length<1||added.Length>4||added.Any(c=>(int)c["type"]!=59||((int)c["levelNumber"]!=0&&(int)c["levelNumber"]!=101)))throw new InvalidOperationException("Existing recovery curves differ.");before=inventory.Except(added).ToArray();
      r["recoverySourceRequestId"]=recoveryId;r["recoverySourceJournalSha256"]=Program.Hash(failedPath);r["intersectionCommandRepeated"]=false;
     }else{PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);before=Inventory(ps);}
     curveCount=before.Count(c=>(int)c["type"]==59);ValidateSurfaceSelection(before,curveCount);
     Dictionary<string,object>[] selected=WorkingSurfaces(before,1);r["selectedWorkingSurfaces"]=selected;r["excludedAuxiliaryEntities"]=before.Where(c=>(int)c["levelNumber"]==2).ToArray();r["selectionRule"]="type=SURFACE and level=1; contour, auxiliary, solid and workplane entities excluded";
     if(phase=="slice"){ps.Exec("PRINCIPALPLANE XY");Select(ps,selected,"SURFACE");TranslationTest.Journal(journal,r,"before_surface_intersection");ps.Exec("CREATE CURVE OBLIQUE");ps.Exec("DISTANCE "+plan.heightsWorldMm[index].ToString("0.######",CultureInfo.InvariantCulture));ps.Exec("ACCEPT");ps.Exec("SELECT CLEARLIST");
      Dictionary<string,object>[] after=Inventory(ps);added=after.Where(c=>!before.Any(b=>Equals(c["compositeId"],b["compositeId"]))).ToArray();if(after.Length!=before.Length+added.Length)throw new InvalidOperationException("Surface inventory count differs.");}
     r["createdInventory"]=added;TranslationTest.Journal(journal,r,"after_surface_intersection");if(added.Length<1||added.Length>4||added.Any(c=>(int)c["type"]!=59))throw new InvalidOperationException("Unexpected surface section output inventory.");
     var readings=added.Select(c=>(Dictionary<string,object>)ReadCurve(ps,c)).ToArray();r["rawCurves"]=readings;TranslationTest.Journal(journal,r,"before_section_validation");r["section"]=ValidateSquare(readings,plan.heightsWorldMm[index]);
     int levelCalls=0;object reference=ps.ActiveDocument;try{dynamic doc=reference;foreach(var c in added)if((int)c["levelNumber"]!=101){levelCalls++;if(!Convert.ToBoolean(doc.SetItemsLevel((int)c["compositeId"],101)))throw new InvalidOperationException("Contour level assignment failed.");}}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
     r["contourLevelAssignmentCalls"]=levelCalls;SettleInventory(ps,curveCount+added.Length);r["totalCurveCount"]=curveCount+added.Length;r["createdCurveCount"]=added.Length;r["zWorldMm"]=plan.heightsWorldMm[index];string result=Path.Combine(folder,"Slices_"+index+".psmodel");r["modelPath"]=result;r["modelSha256"]=SaveClose(ps,result);
    } else if(phase=="verify_surfaces") {
     var last=ReadReceipt(folder,"Slice_24");string saved=(string)last["modelPath"];if(Program.Hash(saved)!=(string)last["modelSha256"])throw new IOException("Final surface model changed.");PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);Dictionary<string,object>[] inventory=Inventory(ps);ValidateSurfaceSelection(inventory,Program.StrictInt(last["totalCurveCount"]));
     var setup=ReadReceipt(folder,"setup");var original=Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(((Dictionary<string,object>)setup["sourceSurfaces"])["faces"]));var results=new List<Dictionary<string,object>>();
     foreach(var face in original){int native=Program.StrictInt(face["nativeId"]),side=Program.StrictInt(face["side"]);var current=inventory.Single(c=>(int)c["type"]==21&&(int)c["nativeId"]==native);if((int)current["levelNumber"]!=(side<4?1:2))throw new InvalidOperationException("Saved surface level differs.");Select(ps,new[]{current},"SURFACE");double[] min=Program.Vector3(ps.Evaluate("SELECTION.MIN_RANGE_EXACT")),max=Program.Vector3(ps.Evaluate("SELECTION.MAX_RANGE_EXACT"));double[] bounds=new[]{min[0],max[0],min[1],max[1],min[2],max[2]};double[] expectedBounds=Program.Json.Deserialize<double[]>(Program.Json.Serialize(face["boundsWorldMm"]));double area=Program.StrictDouble(ps.Evaluate("SURFACE[ID "+native+"].AREA"));if(!TranslationTest.Same(bounds,expectedBounds)||Math.Abs(area-Program.StrictDouble(face["areaRaw"]))>1e-6)throw new InvalidOperationException("Original surface geometry changed during slicing.");results.Add(new Dictionary<string,object>{{"side",side},{"nativeId",native},{"boundsWorldMm",bounds},{"areaRaw",area},{"levelNumber",current["levelNumber"]}});}
     ps.Exec("SELECT CLEARLIST");r["verifiedSourceSurfaces"]=results.ToArray();r["surfaceCount"]=6;r["sourceSurfacesUnchangedOnFinalDiskReopen"]=true;DiskVerification.CloseAudit(ps,working);
    } else if(phase=="export"||phase=="recover_export") {
     var previous=ReadReceipt(folder,"Slice_24");string saved=(string)previous["modelPath"];if(Program.Hash(saved)!=(string)previous["modelSha256"])throw new IOException("Final surface model changed.");string dgkFolder=folder;
     if(phase=="recover_export"){
      string failedPath=Path.Combine(folder,recoveryId+".json");var failed=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(failedPath,Encoding.UTF8));if(!Equals(failed["state"],"surface_phase_in_progress")||!Equals(failed["requestedPhase"],"export")||!Equals(failed["jobId"],jobId)||!Equals(failed["planSha256"],planHash)||Program.Hash(initialPath)!=(string)previous["modelSha256"]||File.Exists(Path.Combine(folder,GroupedExport.FileName("1:Vneshnie 0.8")))||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Independent export recovery context differs.");
      working=initialPath;dgkFolder=Path.Combine(folder,"RecoveredExport_"+requestId);Directory.CreateDirectory(dgkFolder);r["recoverySourceRequestId"]=recoveryId;r["recoverySourceJournalSha256"]=Program.Hash(failedPath);r["intersectionCommandRepeated"]=false;r["priorExportFileAbsentWhenInspected"]=true;
     }else{PlannedTranslation.CopyFrozen(saved,working);TranslationTest.Journal(journal,r,"before_open_export_copy");CheckpointTest.Open(ps,working);}
     Dictionary<string,object>[] inventory=Inventory(ps);int count=Program.StrictInt(previous["totalCurveCount"]);ValidateSurfaceSelection(inventory,count);
     var curves=inventory.Where(c=>(int)c["type"]==59&&(int)c["levelNumber"]==101).ToArray();TranslationTest.Journal(journal,r,"before_select_group");Select(ps,curves,"COMPCURVE");r["selectedCurveCount"]=count;TranslationTest.Journal(journal,r,"after_select_group");
     string dgk=Path.Combine(dgkFolder,GroupedExport.FileName("1:Vneshnie 0.8"));if(File.Exists(dgk))throw new IOException("DGK collision.");r["dgkPath"]=dgk;TranslationTest.Journal(journal,r,"before_group_dgk_write");ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("SELECT CLEARLIST");TranslationTest.Journal(journal,r,"after_group_dgk_write");r["dgkSha256"]=SliceRecipes.ClosedFileHash(dgk);r["curveCount"]=count;r["excludedSurfaceCount"]=6;r["sourceLevel"]="1:Vneshnie 0.8";DiskVerification.CloseAudit(ps,working);
    } else if(phase=="import") {
     var exported=ReadReceipt(folder,"export");string dgk=(string)exported["dgkPath"];if(Program.Hash(dgk)!=(string)exported["dgkSha256"])throw new IOException("Surface DGK changed.");int windows=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=windows+1||Inventory(ps).Length!=0)throw new InvalidOperationException("Import target differs.");ps.Exec("FILE IMPORT \""+dgk+"\"");ps.Exec("DISMISS");Dictionary<string,object>[] inventory=Inventory(ps);if(inventory.Length!=Program.StrictInt(exported["curveCount"])||inventory.Any(c=>(int)c["type"]!=59))throw new InvalidOperationException("DGK contains unexpected entities.");
     foreach(var c in inventory)c["zWorldMm"]=Program.Vector3(ps.Evaluate("COMPCURVE[ID "+(int)c["nativeId"]+"].MIN_RANGE"))[2];r["inventory"]=inventory;r["curveCount"]=inventory.Length;r["surfacesInDgk"]=0;string saved=Path.Combine(folder,"Imported.psmodel");r["modelPath"]=saved;r["modelSha256"]=SaveClose(ps,saved);
    } else {
     var imported=ReadReceipt(folder,"import");string saved=(string)imported["modelPath"];if(Program.Hash(saved)!=(string)imported["modelSha256"])throw new IOException("Imported surface contours changed.");PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);Dictionary<string,object>[] inventory=Inventory(ps);if(inventory.Length!=Program.StrictInt(imported["curveCount"])||inventory.Any(c=>(int)c["type"]!=59))throw new InvalidOperationException("Reopened DGK inventory differs.");
     var recorded=Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(imported["inventory"]));double z=plan.heightsWorldMm[index];var group=recorded.Where(c=>Math.Abs(Program.StrictDouble(c["zWorldMm"])-z)<1e-6).Select(c=>inventory.Single(x=>(int)x["nativeId"]==Program.StrictInt(c["nativeId"])&&Equals(x["name"],c["name"]))).ToArray();r["section"]=ValidateSquare(group.Select(c=>(Dictionary<string,object>)ReadCurve(ps,c)).ToArray(),z);r["zWorldMm"]=z;DiskVerification.CloseAudit(ps,working);
    }
    TranslationTest.ValidateSandbox(config);if(Program.Hash(sourcePath)!=(string)source["importedProjectSha256"]||Program.Hash(sourceReceipt)!=plan.sourceReceiptSha256||CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Surface source or baseline context changed.");
    r["protectedSourceHashesUnchanged"]=true;r["testWindowClosed"]=true;r["state"]="succeeded_surface_section_phase";TranslationTest.Journal(journal,r,"completed");PlannedTranslation.SaveJson(Path.Combine(folder,stable+".json"),r);
   }
  }catch(Exception error){r["state"]=started?"surface_outcome_unknown_requires_inspection":"rejected_before_open";r["error"]=error.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
  finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}return r;
 }
}
}

