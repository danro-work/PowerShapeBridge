using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PowerShapeBridge {
 // Binding preparation/validation are passive. The retained COM context also
 // exposes explicit methods used only by separately guarded mutation executors.
 public static class DocumentBindings {
  public const string Qualification="Observed active owned document binding only. One retained COM application/document and file leases during this check; no later edit authorization, continuous lifecycle tracking or full topology identity. Execution remains disabled.";
  internal interface IContext : IDisposable {
   Dictionary<string,object> Windows();
   Dictionary<string,object> Observation();
   void ConfirmDocument();
  }
  internal sealed class ComContext : OwnedCurveScaleMeasurements.IPhaseGuard, OwnedCurveTranslation.IContext, NativeCurveLayout.IContext, OwnedSurfaceScaleMeasurements.IPhaseGuard, OwnedSurfaceRotation.IWriter,OwnedTranslation.IContext,OwnedSaveAs.IContext,RotationMeasurements.IContext,SolidRotationContext.IWriteContext,OwnedSolidScale.IExecutionContext,NativeSelectionReading.IContext,NativeLevelReading.IContext,OwnedLevelMoveExecution.IContext,OwnedWorkplanePlans.IContext,OwnedWorkplaneActivation.IContext,OwnedSelection.IContext,NativeSurfaceSampling.IContext {
   object application,document;
   internal ComContext(Config config) {
    try {application=DocumentSession.Attach(config);dynamic ps=application;if(!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("No active model document.");document=ps.ActiveDocument;ConfirmDocument();}
    catch {Dispose();throw;}
   }
   public Dictionary<string,object> Windows(){return DocumentSession.ReadStable(application);}
   public Dictionary<string,object> Observation(){return Probe.ReadInContext(application,document);}
   public object ModelItems(){ConfirmDocument();dynamic doc=document;return doc.GetModelItems();}
   public int SelectionCount(){ConfirmDocument();dynamic ps=application;return Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"));}
   public void ChangeSelection(OwnedSelectionProgram program){ConfirmDocument();string command=program.Take();dynamic ps=application;ps.Exec(command);}
   public object SelectedItems(){ConfirmDocument();dynamic doc=document;return doc.GetSelectedItems();}
   public object WorkplaneIds(){ConfirmDocument();dynamic doc=document;return doc.GetWorkplanes((object)3,(object)"",(object)"");}
   public object WorkplaneInfo(int composite){if(composite<=0)throw new ArgumentException("Positive workplane composite required.");ConfirmDocument();dynamic doc=document;return doc.GetWorkplaneInfo(composite);}
   public int ActiveWorkplane(){ConfirmDocument();dynamic doc=document;return Program.StrictInt(doc.ActiveWorkplane);}
   public object CreateWorkplane(double[] frame){PowerShape.Shared.WorkplaneFrame.Validate(frame);ConfirmDocument();dynamic doc=document;return doc.CreateWorkplane(frame.Cast<object>().ToArray());}
   public void ActivateWorld(){ConfirmDocument();dynamic doc=document;doc.ActiveWorkplane=0;}
   public void ActivateWorkplane(int composite){if(composite<0)throw new ArgumentException("Nonnegative explicit workplane composite required.");ConfirmDocument();dynamic doc=document;doc.ActiveWorkplane=composite;}
   public object LevelFields(){ConfirmDocument();dynamic ps=application;object[] substitutions=Enumerable.Range(0,1000).Select(number=>(object)number.ToString(CultureInfo.InvariantCulture)).ToArray();return new object[]{ps.EvaluateArray("LEVEL[*].NAME",1000,substitutions),ps.EvaluateArray("LEVEL[*].USED",1000,substitutions),ps.EvaluateArray("LEVEL[*].ACTIVE",1000,substitutions)};}
   public object LevelFilters(){ConfirmDocument();dynamic ps=application;return new object[]{ps.Evaluate("LEVEL.FILTERED.USED"),ps.Evaluate("LEVEL.FILTERED.NAMED"),ps.Evaluate("LEVEL.FILTERED.ON")};}
   public object LevelSample(int number){if(number<0||number>999)throw new ArgumentException("Native level number outside range.");ConfirmDocument();dynamic ps=application;string prefix="LEVEL["+number.ToString(CultureInfo.InvariantCulture)+"]";return new object[]{ps.Evaluate(prefix+".NAME"),ps.Evaluate(prefix+".USED"),ps.Evaluate(prefix+".ACTIVE")};}
   public object ItemData(object composites){ConfirmDocument();dynamic doc=document;object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(composites,out types,out ids,out names,out descriptions,out levels);return new object[]{types,ids,names,levels};}
   public int Composite(int type,int nativeId) {
    ConfirmDocument();dynamic doc=document;object[] items=Program.ArrayValues((object)doc.GetModelItems());object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);var ts=Program.ArrayValues(types);var ns=Program.ArrayValues(ids);if(ts.Length!=items.Length||ns.Length!=items.Length)throw new InvalidOperationException("Incomplete direct item mapping.");var matched=Enumerable.Range(0,items.Length).Where(i=>Program.StrictInt(ts[i])==type&&Program.StrictInt(ns[i])==nativeId).ToArray();if(matched.Length!=1)throw new InvalidOperationException("Direct item identity absent/ambiguous.");return Program.StrictInt(items[matched[0]]);
   }
   public double[] Bounds(int composite){ConfirmDocument();dynamic doc=document;return TranslationTest.Bounds(doc.GetItemBoundingBox(composite,false));}
   public int Translate(int composite,double[] vector){ConfirmDocument();dynamic doc=document;return Program.StrictInt(doc.TranslateItem(composite,vector[0],vector[1],vector[2]));}
   // Called only through the internal guarded rotation adapter; no public queue route yet.
   public int Rotate(int composite,int angle,double[] axis){if(composite<=0||axis==null||axis.Length!=6||axis.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||Math.Abs(axis.Skip(3).Sum(v=>v*v)-1)>1e-12||angle%360==0||Math.Abs((long)angle)>1000000)throw new ArgumentException("Explicit finite normalized rotation required.");ConfirmDocument();dynamic doc=document;return Program.StrictInt(doc.RotateItem(composite,angle,axis.Cast<object>().ToArray()));}
   public void ScalePhaseGuard(Dictionary<string,object> expected,string solidName,int phase,int windowCount){ScalePhaseGuardCore(expected,solidName,phase,windowCount,"SOLID");}
   public void SurfaceScalePhaseGuard(Dictionary<string,object> expected,string name,int phase,int windowCount){ScalePhaseGuardCore(expected,name,phase,windowCount,"SURFACE");}
   public void CurveScalePhaseGuard(Dictionary<string,object> expected,string name,int phase,int windowCount){ScalePhaseGuardCore(expected,name,phase,windowCount,"COMPCURVE");}
    internal static bool ScaleSelectionMatches(string expectedType,string expectedName,string nativeType,string nativeName){return string.Equals(nativeType,expectedType=="COMPCURVE"?"Composite Curve":expectedType,StringComparison.OrdinalIgnoreCase)&&nativeName==expectedName;}
   void ScalePhaseGuardCore(Dictionary<string,object> expected,string solidName,int phase,int windowCount,string expectedType){
    if(phase<0||phase>=13||windowCount<1)throw new ArgumentException("Fixed scale phase/window required.");ConfirmDocument();dynamic ps=application;var model=expected["model"] as Dictionary<string,object>;if(model==null)throw new InvalidDataException("Pinned scale model required.");int id=Program.StrictInt(model["id"]);
    if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=windowCount||Convert.ToString(ps.Evaluate("MODEL.SELECTED"),CultureInfo.InvariantCulture)!=(string)model["name"]||!string.Equals(Path.GetFullPath(Convert.ToString(ps.Evaluate("MODEL[ID "+id.ToString(CultureInfo.InvariantCulture)+"].PATH"),CultureInfo.InvariantCulture)),Path.GetFullPath((string)model["path"]),StringComparison.OrdinalIgnoreCase)||ActiveWorkplane()!=0||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=(phase==0?0:1))throw new InvalidOperationException("Pinned scale document/WORLD/selection changed.");
    if(phase>0&&(!ScaleSelectionMatches(expectedType,solidName,Convert.ToString(ps.Evaluate("SELECTION.TYPE[0]"),CultureInfo.InvariantCulture),Convert.ToString(ps.Evaluate("SELECTION.NAME[0]"),CultureInfo.InvariantCulture))))throw new InvalidOperationException("Pinned selected scale target changed.");ConfirmDocument();
    string modelName=(string)model["name"];if(modelName.Any(ch=>!char.IsLetterOrDigit(ch)&&ch!='_')||Program.StrictInt(ps.Evaluate("MODEL['"+modelName+"'].ID"))!=id)throw new InvalidOperationException("Pinned owned scale model ID changed.");
   }
   // Internal typed frozen program; never a public raw-command queue route.
   public void ScalePhase(SolidUniformScaleProgram program,int phase){ConfirmDocument();string command=program.Take(phase);dynamic ps=application;ps.Exec(command);}
   public object SetItemsLevel(int composite,int level){if(level<0||level>997)throw new ArgumentException("Level outside qualified candidate range.");ConfirmDocument();dynamic doc=document;return doc.SetItemsLevel(composite,level);}
   public object SurfacePoints(string name,double tStart,double tEnd,int tCount,double uStart,double uEnd,int uCount){ConfirmDocument();dynamic doc=document;return doc.GetSurfacePoints(name,1,tStart,tEnd,tCount,uStart,uEnd,uCount);}
   public int CurvePointCount(int nativeId){if(nativeId<=0)throw new ArgumentException("Positive curve nativeID required.");ConfirmDocument();dynamic ps=application;return Program.StrictInt(ps.Evaluate("COMPCURVE[ID "+nativeId.ToString(CultureInfo.InvariantCulture)+"].POINT.NUMBER"));}
   public object CurvePoints(string name,int start,int end){ConfirmDocument();dynamic doc=document;return doc.GetCompCurvePointData(name,start,end,1);}
   public object ClosestSolidFace(int nativeId,double[] point){if(nativeId<0)throw new ArgumentException("Explicit SOLID ID required.");string xyz=SolidFaceMeasurements.Coordinates(point);ConfirmDocument();dynamic ps=application;return ps.Evaluate("SOLID[ID "+nativeId.ToString(CultureInfo.InvariantCulture)+"].CLOSEST_FACE("+xyz+")");}
   public object FaceNear(string face,double[] point){face=SolidFaceMeasurements.FaceName(face);string xyz=SolidFaceMeasurements.Coordinates(point);ConfirmDocument();dynamic ps=application;return ps.Evaluate("SURFACE['"+face+"'].NEAR("+xyz+")");}
   public object FacePoint(string face,double t,double u,bool normal){face=SolidFaceMeasurements.FaceName(face);SolidFaceMeasurements.Vector(new[]{t,u,0});ConfirmDocument();dynamic ps=application;return ps.Evaluate("SURFACE['"+face+"'].EVALUATE("+t.ToString("R",CultureInfo.InvariantCulture)+";"+u.ToString("R",CultureInfo.InvariantCulture)+")."+(normal?"NORMAL":"POSITION"));}
   public void SaveAs(string path){ConfirmDocument();WorkingCopyOpen.CommandPath(path);dynamic ps=application;ps.Exec("FILE SAVEASPATH "+path);}
   public void ConfirmDocument() {
    dynamic ps=application;if(Convert.ToBoolean(ps.Busy())||!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("Active COM document unavailable/busy.");
    object current=null;IntPtr retained=IntPtr.Zero,active=IntPtr.Zero;
    try {current=ps.ActiveDocument;retained=Marshal.GetIUnknownForObject(document);active=Marshal.GetIUnknownForObject(current);if(retained!=active)throw new InvalidOperationException("Active COM document changed.");}
    finally {if(active!=IntPtr.Zero)Marshal.Release(active);if(retained!=IntPtr.Zero)Marshal.Release(retained);if(current!=null&&Marshal.IsComObject(current))Marshal.ReleaseComObject(current);}
   }
   public void Dispose(){if(document!=null&&Marshal.IsComObject(document))Marshal.ReleaseComObject(document);document=null;if(application!=null&&Marshal.IsComObject(application))Marshal.ReleaseComObject(application);application=null;}
  }
  static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static string Digest(object value){return Sha(Encoding.UTF8.GetBytes(EntitySets.Canonical(value)));}
  internal static string WindowDigest(Dictionary<string,object> windows) {
   foreach(string field in new[]{"processId","processStartedUtc","windowsSessionId","windowCount","windows","activeWindow"})if(!windows.ContainsKey(field))throw new InvalidOperationException("Incomplete window context.");
   return Digest(new object[]{windows["processId"],windows["processStartedUtc"],windows["windowsSessionId"],windows["windowCount"],windows["windows"],windows["activeWindow"]});
  }
  public static void ValidatePrepare(Dictionary<string,object> args) {
   if(args.Count!=5||!args.ContainsKey("copyId")||!EntitySets.Hex(args["copyId"],32)||!args.ContainsKey("ownershipSha256")||!EntitySets.Hex(args["ownershipSha256"],64)||!args.ContainsKey("inspectionRequestId")||!EntitySets.Hex(args["inspectionRequestId"],32)||!args.ContainsKey("inspectionSha256")||!EntitySets.Hex(args["inspectionSha256"],64)||!args.ContainsKey("expiresUtc"))throw new ArgumentException("Invalid document binding parameters.");
   EntitySets.Expiry(args["expiresUtc"]);
  }
  public static void ValidateHandle(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("bindingId")||!EntitySets.Hex(args["bindingId"],32)||!args.ContainsKey("bindingSha256")||!EntitySets.Hex(args["bindingSha256"],64))throw new ArgumentException("Invalid document binding identity.");
  }
  static string BindingPath(string root,string id) {
   if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid binding identity.");
   string path=Path.Combine(OwnedCopies.LocalPath(root.TrimEnd('\\','/')),"documentbindings",id+".json");OwnedCopies.NoReparse(path);return path;
  }
  internal static Dictionary<string,object> Load(string root,Dictionary<string,object> args) {
   ValidateHandle(args);byte[] bytes;
   using(var file=new FileStream(BindingPath(root,(string)args["bindingId"]),FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(file.Length>65536)throw new IOException("Document binding exceeds limit.");using(var memory=new MemoryStream()){file.CopyTo(memory);bytes=memory.ToArray();}
   }
   if(Sha(bytes)!=(string)args["bindingSha256"])throw new IOException("Document binding receipt changed.");
   var plan=Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
   if(plan==null||plan.Count!=12||!Equals(plan["schemaVersion"],1)||!Equals(plan["bindingId"],args["bindingId"])||!Equals(plan["executionEnabled"],false)||!EntitySets.Hex(plan["observedPropertiesSha256"],64)||!EntitySets.Hex(plan["windowContextSha256"],64))throw new IOException("Invalid document binding receipt.");
   ValidatePrepare(Parameters(plan));return plan;
  }
  static Dictionary<string,object> Parameters(Dictionary<string,object> plan){return new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","expiresUtc"}.ToDictionary(k=>k,k=>plan[k]);}
  internal static void Check(OwnedCopies.OwnershipLease lease,Dictionary<string,object> expected,DateTime expiry,IContext context,Dictionary<string,object> diagnostics,string pinnedWindows=null) {
   EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
   var before=context.Windows();string windowsSha=WindowDigest(before);
   if(pinnedWindows!=null&&windowsSha!=pinnedWindows)throw new InvalidOperationException("Bound window/process context changed.");
   var first=context.Observation();EntitySets.CheckObserved(expected,first,new int[0][],expiry,DateTime.UtcNow);
   var second=context.Observation();EntitySets.CheckObserved(expected,second,new int[0][],expiry,DateTime.UtcNow);
   var fresh=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(second));var model=(Dictionary<string,object>)fresh["model"];
   diagnostics["observedPropertiesSha256"]=EntitySets.ObservedDigest(fresh);diagnostics["observedModel"]=model;diagnostics["windowContextSha256"]=windowsSha;diagnostics["sameContextObservationReads"]=2;
   if(WindowDigest(context.Windows())!=windowsSha)throw new InvalidOperationException("Window context changed during binding.");
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(before[key],fresh[key]))throw new InvalidOperationException("Window and model process identity disagree.");
   var active=before["activeWindow"] as Dictionary<string,object>;
   if(active==null||!active.ContainsKey("modelId")||!Equals(active["modelId"],model["id"])||!Equals(active["modelName"],model["name"])||!string.Equals(OwnedCopies.LocalPath((string)active["modelPath"]),OwnedCopies.LocalPath((string)model["path"]),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Active model/window identity disagree.");
   var windows=Program.Json.Deserialize<List<Dictionary<string,object>>>(Program.Json.Serialize(before["windows"]));
   if(windows.Count(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],model["id"]))!=1||windows.Count(w=>w.ContainsKey("modelPath")&&string.Equals(OwnedCopies.LocalPath((string)w["modelPath"]),lease.WorkingPath,StringComparison.OrdinalIgnoreCase))!=1)throw new InvalidOperationException("active_document_not_owned_working_copy");
   lease.CheckPath((string)model["path"]);lease.Recheck();context.ConfirmDocument();
   if(WindowDigest(context.Windows())!=windowsSha||DateTime.UtcNow>=expiry)throw new InvalidOperationException("Context/lease changed at final binding check.");
   diagnostics["checkpointVerified"]=true;diagnostics["workingSha256"]=lease.Inspection["workingSha256"];
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args,Dictionary<string,object> expected,IContext context,Dictionary<string,object> diagnostics) {
   ValidatePrepare(args);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid binding request ID.");
   string path=BindingPath(root,id);if(File.Exists(path))throw new IOException("Binding identity already exists; replay forbidden.");
   using(var lease=OwnedCopies.Hold(root,(string)args["copyId"],(string)args["ownershipSha256"])) {
    Check(lease,expected,EntitySets.Expiry(args["expiresUtc"]),context,diagnostics);
    var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"bindingId",id},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"observedPropertiesSha256",diagnostics["observedPropertiesSha256"]},{"windowContextSha256",diagnostics["windowContextSha256"]},{"executionEnabled",false}};
    Directory.CreateDirectory(Path.GetDirectoryName(path));OwnedCopies.NoReparse(path);byte[] bytes=Encoding.UTF8.GetBytes(Program.Json.Serialize(plan));
    using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
    diagnostics["state"]="working_document_bound_observed";diagnostics["bindingId"]=id;diagnostics["bindingSha256"]=Sha(bytes);diagnostics["binding"]=plan;return diagnostics;
   }
  }
  public static Dictionary<string,object> Run(string requestId,Dictionary<string,object> args,bool prepare) {
   var result=new Dictionary<string,object>{{"state","working_document_binding_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"cadMutationAttempted",false},{"contextChangeAttempted",false},{"powerShapeConnectionAttempted",false},{"executionEnabled",false},{"qualification",Qualification}};
   try {
    Config config=Program.ReadConfig();if(!config.IsGeneralCore||!config.documentBindingsExperimental)throw new InvalidOperationException("Experimental document bindings disabled.");
    Dictionary<string,object> plan=null,parameters;if(prepare){ValidatePrepare(args);parameters=args;}else{plan=Load(Program.Root,args);parameters=Parameters(plan);}
    var expected=EntitySets.Receipt((string)parameters["inspectionRequestId"],(string)parameters["inspectionSha256"]);var expiry=EntitySets.Expiry(parameters["expiresUtc"]);
    EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
    using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     result["powerShapeConnectionAttempted"]=true;
     using(var context=new ComContext(config)) {
      if(prepare)Prepare(Program.Root,requestId,parameters,expected,context,result);
      else using(var lease=OwnedCopies.Hold(Program.Root,(string)parameters["copyId"],(string)parameters["ownershipSha256"])) {
       if(EntitySets.ObservedDigest(expected)!=(string)plan["observedPropertiesSha256"])throw new IOException("Binding observation digest differs from source receipt.");
       Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);result["state"]="working_document_binding_validated_observed";result["bindingId"]=args["bindingId"];result["bindingSha256"]=args["bindingSha256"];result["binding"]=plan;
      }
     }
    }
   }catch(Exception error) {
    result["error"]=error.Message;result["errorCode"]=error.Message=="active_document_not_owned_working_copy"?error.Message:"working_document_binding_policy_or_context_rejected";
    if(prepare){result["bindingId"]=requestId;result["localArtifactsMayExist"]=true;}
   }
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  sealed class FakeContext : IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal Action<int> DuringRead;internal int Reads;internal bool Confirmed,Disposed;
   public Dictionary<string,object> Windows(){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(Listing));}
   public Dictionary<string,object> Observation(){Reads++;if(DuringRead!=null)DuringRead(Reads);return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(Snapshot));}
   public void ConfirmDocument(){Confirmed=true;}
   public void Dispose(){Disposed=true;}
  }
  public static int SelfTest() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Document binding self-test failed at "+count);count++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}check(rejected);};
   string root=Path.Combine(Program.Root,"work","binding-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string source=Path.Combine(root,"Source.psmodel");File.WriteAllText(source,"Owned opaque file fixture, never opened as CAD.");string copyId=Guid.NewGuid().ToString("N");var copy=OwnedCopies.Create(root,copyId,source,Program.Hash(source));var ownership=(Dictionary<string,object>)copy["ownership"];string working=(string)((Dictionary<string,object>)ownership["working"])["path"];
   var snapshot=new Dictionary<string,object>();foreach(string key in new[]{"levelsContainingEntities","selectionBefore","selectionAfter","workplanes"})snapshot[key]=new object[0];foreach(string key in new[]{"lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})snapshot[key]=null;
   snapshot["state"]="succeeded_verified";snapshot["modelScope"]="active_document_read_only";snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");snapshot["processId"]=1;snapshot["processStartedUtc"]="2026-10-09T10:00:00Z";snapshot["windowsSessionId"]=1;snapshot["productVersion"]="23.2.2";snapshot["model"]=new Dictionary<string,object>{{"id",9},{"name","Working"},{"path",working}};snapshot["entities"]=new object[0];snapshot["entityCount"]=0;snapshot["contextBefore"]=new[]{0,0};snapshot["contextAfter"]=new[]{0,0};
   var window=new Dictionary<string,object>{{"windowNumber",1},{"nativeWindowId",3},{"type","Model"},{"selectedRaw",1},{"modelId",9},{"modelName","Working"},{"modelPath",working}};
   var listing=new Dictionary<string,object>{{"processId",1},{"processStartedUtc",snapshot["processStartedUtc"]},{"windowsSessionId",1},{"windowCount",1},{"windows",new[]{window}},{"activeWindow",window}};
   Func<Dictionary<string,object>,Dictionary<string,object>> clone=v=>Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));
   Func<FakeContext> context=()=>new FakeContext{Snapshot=clone(snapshot),Listing=clone(listing)};
   var args=new Dictionary<string,object>{{"copyId",copyId},{"ownershipSha256",copy["ownershipSha256"]},{"inspectionRequestId",new string('a',32)},{"inspectionSha256",new string('b',64)},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}};
   string id=Guid.NewGuid().ToString("N");var fake=context();var result=Prepare(root,id,args,snapshot,fake,new Dictionary<string,object>());check(Equals(result["state"],"working_document_bound_observed")&&fake.Reads==2&&fake.Confirmed&&!fake.Disposed);var handle=new Dictionary<string,object>{{"bindingId",id},{"bindingSha256",result["bindingSha256"]}};var plan=Load(root,handle);check(Equals(plan["executionEnabled"],false));
   using(var lease=OwnedCopies.Hold(root,copyId,(string)copy["ownershipSha256"])) {
    Check(lease,snapshot,EntitySets.Expiry(args["expiresUtc"]),context(),new Dictionary<string,object>(),(string)plan["windowContextSha256"]);count++;
    // The real file leases deny replacement/checkpoint writes while the context reads.
    rejects(()=>File.Delete(working));rejects(()=>File.WriteAllText(Path.Combine(root,"workingcopies",copyId,"ownership.json"),"changed"));
    string checkpoint=(string)((Dictionary<string,object>)ownership["checkpoint"])["path"];File.SetAttributes(checkpoint,FileAttributes.Normal);rejects(()=>new FileStream(checkpoint,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());rejects(()=>lease.Recheck());File.SetAttributes(checkpoint,FileAttributes.ReadOnly);lease.Recheck();count++;
    foreach(string field in new[]{"processId","processStartedUtc","windowsSessionId","model","entityCount","contextBefore","selectionBefore","workplanes","lengthUnitFactorRaw"}) {
     var changed=context();changed.Snapshot[field]=field=="processStartedUtc"?(object)"other":field=="model"?(object)new Dictionary<string,object>{{"id",10},{"name","Working"},{"path",working}}:field=="workplanes"||field=="selectionBefore"||field=="contextBefore"?(object)new[]{1}:2;
     rejects(()=>Check(lease,snapshot,EntitySets.Expiry(args["expiresUtc"]),changed,new Dictionary<string,object>()));
    }
    var switched=context();switched.DuringRead=n=>{if(n==2)switched.Snapshot["processId"]=2;};rejects(()=>Check(lease,snapshot,EntitySets.Expiry(args["expiresUtc"]),switched,new Dictionary<string,object>()));
    var otherWindow=context();otherWindow.Listing["windowCount"]=2;rejects(()=>Check(lease,snapshot,EntitySets.Expiry(args["expiresUtc"]),otherWindow,new Dictionary<string,object>(),(string)plan["windowContextSha256"]));
    rejects(()=>Check(lease,snapshot,DateTime.UtcNow.AddSeconds(-1),context(),new Dictionary<string,object>()));
   }
   foreach(string target in new[]{source,(string)((Dictionary<string,object>)ownership["checkpoint"])["path"],Path.Combine(root,"Foreign.psmodel")}) {
    var wrong=context();((Dictionary<string,object>)wrong.Snapshot["model"])["path"]=target;var wrongExpected=clone(wrong.Snapshot);((Dictionary<string,object>)wrong.Listing["activeWindow"])["modelPath"]=target;wrong.Listing["windows"]=new[]{wrong.Listing["activeWindow"]};string rejectedId=Guid.NewGuid().ToString("N");rejects(()=>Prepare(root,rejectedId,args,wrongExpected,wrong,new Dictionary<string,object>()));check(!File.Exists(BindingPath(root,rejectedId)));
   }
   rejects(()=>Prepare(root,id,args,snapshot,context(),new Dictionary<string,object>()));
   var extra=new Dictionary<string,object>(args){{"command","SAVE"}};rejects(()=>ValidatePrepare(extra));rejects(()=>ValidateHandle(new Dictionary<string,object>{{"bindingId","../bad"},{"bindingSha256",new string('a',64)}}));
   File.AppendAllText(BindingPath(root,id)," ");rejects(()=>Load(root,handle));
   if(Program.ReadConfig().documentBindingsExperimental) {
    foreach(string recipe in new[]{"bind_working_document","validate_working_document_binding"}){FileRequests.Parse(new string('d',32),Program.Json.Serialize(new{requestId=new string('d',32),operation="recipe.prepare",arguments=new{recipeId=recipe,parameters=recipe=="bind_working_document"?args:handle}}));count++;}
    rejects(()=>FileRequests.Parse(new string('d',32),Program.Json.Serialize(new{requestId=new string('d',32),operation="recipe.prepare",arguments=new{recipeId="bind_working_document",parameters=extra}})));
   }
   return count;
  }
 }
}

