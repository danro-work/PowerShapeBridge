using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PowerShapeBridge {
// Qualification only: no production model parameters or technological rules.
public static class BatchSectionQualification {
 static Dictionary<string,object>[] StableInventory(object application) {
  Exception last=null;for(int n=0;n<30;n++){try{return SurfaceSectionJob.Inventory(application);}catch(InvalidOperationException e){last=e;}catch(FormatException e){last=e;}System.Threading.Thread.Sleep(100);}throw new InvalidOperationException("Inventory read did not settle: "+last.Message);
 }
 public static Dictionary<string,object> InspectFailed(string batchId,string failedId) {
  Guid g;if(!Guid.TryParseExact(batchId,"N",out g)||!Guid.TryParseExact(failedId,"N",out g))throw new ArgumentException("Invalid failure identity.");
  string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","BatchSections_"+batchId));string working=Path.Combine(folder,"Working_"+failedId+".psmodel");
  object app=null;try{app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;if(CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Failed copy not active.");
   object unit=ps.Evaluate("unit[length].name");return new Dictionary<string,object>{{"state","inspected_batch_failure"},{"activePath",working},{"unitNameRaw",unit},{"unitRuntimeType",unit==null?"null":unit.GetType().FullName},{"busy",Convert.ToBoolean(ps.Busy())},{"inventory",SurfaceSectionJob.Inventory((object)ps,false)}};
  }finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 }
 static double[] Bounds(object application,Dictionary<string,object> entity) {
  dynamic ps=application;SurfaceSectionJob.Select(ps,new[]{entity},"SURFACE");
  try {double[] min=Program.Vector3(ps.Evaluate("SELECTION.MIN_RANGE_EXACT")),max=Program.Vector3(ps.Evaluate("SELECTION.MAX_RANGE_EXACT"));return new[]{min[0],max[0],min[1],max[1],min[2],max[2]};}finally{ps.Exec("SELECT CLEARLIST");}
 }
 static Dictionary<string,object>[] SourceMeasures(dynamic ps) {
  var entities=StableInventory((object)ps).Where(c=>(int)c["type"]==21&&(int)c["levelNumber"]!=102).ToArray();
  if(entities.Length!=6||SurfaceSectionJob.WorkingSurfaces(entities,1).Length!=4||entities.Count(c=>(int)c["levelNumber"]==2)!=2)throw new InvalidOperationException("Qualified six-surface fixture differs.");
  object reference=ps.ActiveDocument;try {dynamic doc=reference;return entities.Select(c=>new Dictionary<string,object>{{"compositeId",c["compositeId"]},{"levelNumber",c["levelNumber"]},{"boundsWorldMm",(double[])Bounds((object)ps,c)},{"areaRaw",Program.StrictDouble(ps.Evaluate("SURFACE[ID "+c["nativeId"]+"].AREA"))}}).ToArray();}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 }
 static void SameSource(Dictionary<string,object>[] before,Dictionary<string,object>[] after) {
  foreach(var b in before){var a=after.Single(c=>Equals(c["compositeId"],b["compositeId"]));if(!Equals(a["levelNumber"],b["levelNumber"])||Math.Abs(Program.StrictDouble(a["areaRaw"])-Program.StrictDouble(b["areaRaw"]))>1e-6||!TranslationTest.Same(Program.Json.Deserialize<double[]>(Program.Json.Serialize(a["boundsWorldMm"])),Program.Json.Deserialize<double[]>(Program.Json.Serialize(b["boundsWorldMm"]))))throw new InvalidOperationException("Source measurements changed.");}
 }
 static Dictionary<string,object>[] Sections(dynamic ps,double[] heights) {
  var curves=StableInventory((object)ps).Where(c=>(int)c["type"]==59).Select(c=>(Dictionary<string,object>)SurfaceSectionJob.ReadCurve(ps,c)).ToArray();
  if(curves.Any(c=>(int)c["levelNumber"]!=101))throw new InvalidOperationException("Curve level differs.");int counted=0;var sections=new List<Dictionary<string,object>>();
  foreach(double z in heights){var group=curves.Where(c=>Math.Abs(((double[])c["boundsWorldMm"])[4]-z)<=1e-6&&Math.Abs(((double[])c["boundsWorldMm"])[5]-z)<=1e-6).ToArray();sections.Add(SurfaceSectionJob.ValidateSquare(group,z));counted+=group.Length;}
  if(counted!=curves.Length)throw new InvalidOperationException("Unexpected heights or duplicates.");return sections.ToArray();
 }
 static Dictionary<string,object> Prior(string folder,string phase) {
  var r=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,phase+".json"),Encoding.UTF8));if(!Equals(r["state"],"succeeded_batch_section_phase"))throw new IOException("Prior phase incomplete.");return r;
 }
 public static Dictionary<string,object> CleanupBeforePlanes(string batchId,string failedId) {
  object app=null;var result=new Dictionary<string,object>{{"state","rejected"},{"geometryMutationAttempted",false}};
  try {
   Guid g;if(!Guid.TryParseExact(batchId,"N",out g)||!Guid.TryParseExact(failedId,"N",out g))throw new ArgumentException("Invalid cleanup identity.");
   string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","BatchSections_"+batchId));string receipt=Path.Combine(folder,failedId+".json");
   var failed=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt,Encoding.UTF8));
   if(!Equals(failed["state"],"batch_outcome_unknown_requires_inspection")||!Equals(failed["requestedPhase"],"planes")||!Equals(failed["error"],"Bounding box failed.")||failed.ContainsKey("sourceSurfacesBefore"))throw new InvalidOperationException("Only the pre-creation bounds failure can be reconciled here.");
   string working=Path.Combine(folder,"Working_"+failedId+".psmodel");if(Program.Hash(working)!=(string)failed["sourceSha256"])throw new IOException("Initial copy bytes changed.");
   using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;if(Convert.ToBoolean(ps.Busy())||CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Failed working copy is not active/idle.");
    var inspection=SurfaceRecipes.Inspect(ps,21);var inventory=StableInventory((object)ps);if(inventory.Length!=6||inventory.Any(c=>(int)c["type"]!=21)||SurfaceSectionJob.WorkingSurfaces(inventory,1).Length!=4||SurfaceSectionJob.WorkingSurfaces(inventory,2).Length!=2)throw new InvalidOperationException("Source-only inventory differs.");
    result["inspection"]=inspection;result["failedJournalSha256"]=Program.Hash(receipt);result["planeCount"]=0;result["curveCount"]=0;DiskVerification.CloseAudit(ps,working);Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);if(CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Baseline not restored.");
    result["state"]="succeeded_precreation_copy_inspected_closed";result["workingWindowClosed"]=true;
   }
  }catch(Exception e){result["error"]=e.Message;}finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}return result;
 }
 public static Dictionary<string,object> Run(string requestId,string batchId,string sourceJobId,string sourceReceiptHash,string phase) {
  object app=null;string journal=null;bool opened=false;var r=new Dictionary<string,object>{{"requestId",requestId},{"batchId",batchId},{"state","rejected_before_open"},{"requestedPhase",phase},{"sourceGeometryMutationAttempted",false}};
  try {
   Guid id;bool nativeArray=phase=="planes_array";if(nativeArray)phase="planes";
   if(phase.StartsWith("recover_array_seed_")){nativeArray=true;}
   string recoveryId=null;if(phase.StartsWith("recover_planes_")){recoveryId=phase.Substring(15);if(!Guid.TryParseExact(recoveryId,"N",out id))throw new ArgumentException("Invalid recovery identity.");phase="planes";}
   if(phase.StartsWith("recover_array_seed_")){recoveryId=phase.Substring(19);if(!Guid.TryParseExact(recoveryId,"N",out id))throw new ArgumentException("Invalid array seed recovery identity.");phase="planes";}
   if(!Guid.TryParseExact(requestId,"N",out id)||!Guid.TryParseExact(batchId,"N",out id)||!Guid.TryParseExact(sourceJobId,"N",out id)||!new[]{"planes","intersect","export","verify"}.Contains(phase))throw new ArgumentException("Invalid batch qualification identity/phase.");
   Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);
   string sourceFolder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","SurfaceJob_"+sourceJobId));string sourceReceipt=Path.Combine(sourceFolder,"setup.json");
   if(Program.Hash(sourceReceipt)!=sourceReceiptHash)throw new IOException("Source receipt changed.");var source=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(sourceReceipt,Encoding.UTF8));
   string fixture=Path.Combine(sourceFolder,"Fixture.psmodel");if(!Equals(source["state"],"succeeded_surface_section_phase")||!Equals(source["modelPath"],fixture)||Program.Hash(fixture)!=(string)source["modelSha256"])throw new IOException("Source fixture changed.");
   string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification","BatchSections_"+batchId));Directory.CreateDirectory(folder);if(File.Exists(Path.Combine(folder,phase+".json")))throw new IOException("Completed phase replay forbidden.");
   using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    using(var claim=new FileStream(Path.Combine(folder,requestId+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
    string expectedActive=recoveryId==null?Path.GetFullPath(config.expectedProject):Path.Combine(folder,"Working_"+recoveryId+".psmodel");
    if(!Process.GetProcessesByName("powershape").Any(p=>p.Id==pid&&p.SessionId==Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy())||CheckpointTest.ModelPath(ps)!=expectedActive)throw new InvalidOperationException("Expected idle baseline/recovery document required.");
    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=config.expectedVersion)throw new InvalidOperationException("Version differs.");
    opened=true;journal=Path.Combine(folder,requestId+".json");r["state"]="batch_phase_in_progress";r["sourceJobId"]=sourceJobId;r["sourceReceiptSha256"]=sourceReceiptHash;r["sourcePath"]=fixture;r["sourceSha256"]=source["modelSha256"];r["processId"]=pid;TranslationTest.Journal(journal,r,"before_"+phase);
    string working=Path.Combine(folder,"Working_"+requestId+".psmodel");double[] heights=Enumerable.Range(0,25).Select(n=>n*800000/1000000.0).ToArray();r["heightsWorldMm"]=heights;
    if(phase!="verify") {
     string previous=fixture;if(phase!="planes"){var prior=Prior(folder,phase=="intersect"?"planes":"intersect");previous=(string)prior["modelPath"];if(Program.Hash(previous)!=(string)prior["modelSha256"])throw new IOException("Prior geometry changed.");}
     Dictionary<string,object> recovery=null;
     if(recoveryId==null){PlannedTranslation.CopyFrozen(previous,working);CheckpointTest.Open(ps,working);}else{
      string failedJournal=Path.Combine(folder,recoveryId+".json");recovery=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(failedJournal,Encoding.UTF8));
      bool recoverable=nativeArray?Equals(recovery["requestedPhase"],"planes_array")&&Equals(recovery["error"],"Invalid integer response.")&&!recovery.ContainsKey("planeArrayInvocationCount"):Equals(recovery["requestedPhase"],"planes")&&Convert.ToString(recovery["error"]).StartsWith("World mm required:");
      if(!Equals(recovery["state"],"batch_outcome_unknown_requires_inspection")||!Equals(recovery["batchId"],batchId)||!recoverable||Program.Hash(expectedActive)!=(string)source["modelSha256"])throw new InvalidOperationException("Plane recovery evidence differs.");
      working=expectedActive;r["recoverySourceRequestId"]=recoveryId;r["recoverySourceJournalSha256"]=Program.Hash(failedJournal);
     }
     var beforeSource=SourceMeasures(ps);r["sourceSurfacesBefore"]=beforeSource;
     if(recovery!=null)SameSource(Program.Json.Deserialize<Dictionary<string,object>[]>(Program.Json.Serialize(recovery["sourceSurfacesBefore"])),beforeSource);
     object reference=ps.ActiveDocument;try {dynamic doc=reference;
      if(phase=="planes") {
       var initial=StableInventory((object)ps);var existing=initial.Where(c=>(int)c["type"]==21&&(int)c["levelNumber"]==102).ToArray();if(initial.Length!=6+existing.Length||existing.Length>25||recovery==null&&existing.Length!=0)throw new InvalidOperationException("Unexpected source/plane objects.");
       double[][] measured=SurfaceSectionJob.WorkingSurfaces(initial,1).Select(c=>(double[])Bounds((object)ps,c)).ToArray();double low=measured.Min(b=>b[4]),high=measured.Max(b=>b[5]);
       r["measuredElementHeightMm"]=high-low;r["measuredRangeWorldMm"]=new[]{low,high};if(Math.Abs(low)>1e-6||Math.Abs(high-20)>1e-6)throw new InvalidOperationException("Qualified element height differs.");
       doc.SetLevelName(102,"Bridge_SectionPlanes");ps.Exec("PRINCIPALPLANE XY");var planes=new List<Dictionary<string,object>>();var timer=Stopwatch.StartNew();
       foreach(var plane in existing){double[] bounds=Bounds((object)ps,plane);double z=heights[planes.Count];if(!TranslationTest.Same(bounds,new[]{-20.0,20.0,-20.0,20.0,z,z}))throw new InvalidOperationException("Existing recovery plane differs.");plane["boundsWorldMm"]=bounds;planes.Add(plane);}
       r["existingPlaneCountInspected"]=planes.Count;r["existingPlanesRecreated"]=false;
       foreach(double z in nativeArray?heights.Take(1):heights) {
        if(planes.Any(p=>Math.Abs(((double[])p["boundsWorldMm"])[4]-z)<1e-6))continue;
        var before=StableInventory((object)ps);ps.Exec("SELECT CLEARLIST");ps.Exec("CREATE SURFACE PLANE");ps.Exec("0 0 "+z.ToString("0.######",CultureInfo.InvariantCulture));
        var added=StableInventory((object)ps).Where(c=>!before.Any(b=>Equals(b["compositeId"],c["compositeId"]))).ToArray();if(added.Length!=1||(int)added[0]["type"]!=21)throw new InvalidOperationException("Expected one plane surface.");
        var plane=added[0];SurfaceSectionJob.Select(ps,added,"SURFACE");ps.Exec("SURFEDITS");ps.Exec("LENGTH 40");ps.Exec("WIDTH 40");ps.Exec("ACCEPT");ps.Exec("SELECT CLEARLIST");
        double[] bounds=Bounds((object)ps,plane);double x=-(bounds[0]+bounds[1])/2,y=-(bounds[2]+bounds[3])/2;
        if(Math.Abs(x)>1e-6||Math.Abs(y)>1e-6)if(Program.StrictInt(doc.TranslateItem((int)plane["compositeId"],x,y,0))!=1)throw new InvalidOperationException("Plane centering failed.");
        bounds=Bounds((object)ps,plane);if(!TranslationTest.Same(bounds,new[]{-20.0,20.0,-20.0,20.0,z,z}))throw new InvalidOperationException("Plane coverage differs.");
        if(!Convert.ToBoolean(doc.SetItemsLevel((int)plane["compositeId"],102)))throw new InvalidOperationException("Plane level failed.");plane["levelNumber"]=102;plane["boundsWorldMm"]=bounds;planes.Add(plane);r["planeCount"]=planes.Count;TranslationTest.Journal(journal,r,"after_section_plane_"+planes.Count);
       }
       if(nativeArray) {
        if(planes.Count!=1)throw new InvalidOperationException("One seed plane required.");SurfaceSectionJob.Select(ps,new[]{planes[0]},"SURFACE");r["planeArrayInvocationCount"]=1;TranslationTest.Journal(journal,r,"before_native_plane_array");
        ps.Exec("EDIT MOVE");ps.Exec("KEEP");ps.Exec("COPIES 24");ps.Exec("X 0");ps.Exec("Y 0");ps.Exec("Z 0.8");ps.Exec("APPLY");ps.Exec("DISMISS");ps.Exec("SELECT CLEARLIST");
        var array=StableInventory((object)ps).Where(c=>(int)c["type"]==21&&(int)c["levelNumber"]==102).ToArray();if(array.Length!=25||StableInventory((object)ps).Length!=31)throw new InvalidOperationException("Native plane array inventory differs.");
        planes.Clear();foreach(var plane in array){plane["boundsWorldMm"]=Bounds((object)ps,plane);planes.Add(plane);}planes=planes.OrderBy(p=>((double[])p["boundsWorldMm"])[4]).ToList();
        for(int n=0;n<25;n++)if(!TranslationTest.Same((double[])planes[n]["boundsWorldMm"],new[]{-20.0,20.0,-20.0,20.0,heights[n],heights[n]}))throw new InvalidOperationException("Native array height or coverage differs.");
       }
       timer.Stop();r["planeCreationMilliseconds"]=timer.ElapsedMilliseconds;r["planeCount"]=planes.Count;r["planeSet"]=planes.ToArray();r["planeCreationMethod"]=nativeArray?"One seed plane plus one native EDIT MOVE / KEEP / COPIES 24, relative Z 0.8 operation.":"Bounded creation in one context; native pattern feature not qualified.";
      } else if(phase=="intersect") {
       var inventory=StableInventory((object)ps);var primary=SurfaceSectionJob.WorkingSurfaces(inventory,1);var secondary=SurfaceSectionJob.WorkingSurfaces(inventory,102);if(primary.Length!=4||secondary.Length!=25||inventory.Length!=31)throw new InvalidOperationException("Primary/secondary sets differ.");
       SurfaceSectionJob.Select(ps,primary,"SURFACE");var timer=Stopwatch.StartNew();r["intersectionInvocationCount"]=1;r["primaryCount"]=4;r["secondaryCount"]=25;TranslationTest.Journal(journal,r,"before_batch_intersection");
       ps.Exec("CREATE CURVE INTERSECT");foreach(var plane in secondary)ps.Exec("ADD SURFACE '"+(string)plane["name"]+"'");ps.Exec("ACCEPT");ps.Exec("SELECT CLEARLIST");timer.Stop();r["intersectionMilliseconds"]=timer.ElapsedMilliseconds;
       var created=StableInventory((object)ps).Where(c=>!inventory.Any(b=>Equals(b["compositeId"],c["compositeId"]))).ToArray();r["createdInventory"]=created;TranslationTest.Journal(journal,r,"after_batch_intersection");
       if(created.Length<25||created.Length>100||created.Any(c=>(int)c["type"]!=59))throw new InvalidOperationException("Unexpected intersection curves.");
       foreach(var curve in created)if(!Convert.ToBoolean(doc.SetItemsLevel((int)curve["compositeId"],101)))throw new InvalidOperationException("Curve level failed.");
       r["sections"]=Sections(ps,heights);foreach(var plane in secondary)if(Program.StrictInt(doc.DeleteItem((int)plane["compositeId"]))!=1)throw new InvalidOperationException("Temporary plane deletion failed.");
       if(StableInventory((object)ps).Length!=6+created.Length)throw new InvalidOperationException("Temporary geometry remains.");r["curveCount"]=created.Length;r["temporaryPlanesRemoved"]=true;r["offsetQualification"]="Dialog default used as in Autodesk wrapper; exact square postconditions checked; explicit offset reset not qualified.";
      } else {
       r["sections"]=Sections(ps,heights);var curves=StableInventory((object)ps).Where(c=>(int)c["type"]==59).ToArray();SurfaceSectionJob.Select(ps,curves,"COMPCURVE");string dgk=Path.Combine(folder,"1-Vneshnie 0.8.dgk");if(File.Exists(dgk))throw new IOException("DGK collision.");
       TranslationTest.Journal(journal,r,"before_export");ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("SELECT CLEARLIST");r["dgkPath"]=dgk;r["dgkSha256"]=SliceRecipes.ClosedFileHash(dgk);r["curveCount"]=curves.Length;
      }
     } finally {if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
     SameSource(beforeSource,SourceMeasures(ps));r["sourceSurfaceMeasurementsUnchanged"]=true;
     if(phase=="export")DiskVerification.CloseAudit(ps,working);else {string saved=Path.Combine(folder,phase+".psmodel");r["modelPath"]=saved;r["modelSha256"]=SurfaceSectionJob.SaveClose(ps,saved);}
    } else {
     var exported=Prior(folder,"export");string dgk=(string)exported["dgkPath"];if(Program.Hash(dgk)!=(string)exported["dgkSha256"])throw new IOException("DGK changed.");
     ps.Exec("FILE NEW");if(StableInventory((object)ps).Length!=0)throw new InvalidOperationException("Import target not empty.");ps.Exec("FILE IMPORT \""+dgk+"\"");ps.Exec("DISMISS");
     object reference=ps.ActiveDocument;try{dynamic doc=reference;foreach(var curve in StableInventory((object)ps)){if((int)curve["type"]!=59)throw new InvalidOperationException("Noncurve DGK entity.");if(!Convert.ToBoolean(doc.SetItemsLevel((int)curve["compositeId"],101)))throw new InvalidOperationException("Imported curve level failed.");}}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
     r["sectionsAfterImport"]=Sections(ps,heights);string saved=Path.Combine(folder,"Imported.psmodel");r["modelPath"]=saved;r["modelSha256"]=SurfaceSectionJob.SaveClose(ps,saved);PlannedTranslation.CopyFrozen(saved,working);CheckpointTest.Open(ps,working);r["sectionsAfterDiskReopen"]=Sections(ps,heights);DiskVerification.CloseAudit(ps,working);r["allSectionsVerifiedAfterDiskReopen"]=true;
    }
    TranslationTest.ValidateSandbox(config);if(Program.Hash(fixture)!=(string)source["modelSha256"]||CheckpointTest.ModelPath(ps)!=Path.GetFullPath(config.expectedProject))throw new InvalidOperationException("Protected fixture/context changed.");
    r["testWindowClosed"]=true;r["protectedSourceHashesUnchanged"]=true;r["state"]="succeeded_batch_section_phase";TranslationTest.Journal(journal,r,"completed");PlannedTranslation.SaveJson(Path.Combine(folder,phase+".json"),r);
   }
  }catch(Exception ex){r["state"]=opened?"batch_outcome_unknown_requires_inspection":"rejected_before_open";r["error"]=ex.Message;r["recovery"]="Inspect the copied model and journal. Never repeat an uncertain intersection.";if(journal!=null)TranslationTest.Journal(journal,r,"stopped");}
  finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}return r;
 }
}
}
