using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using PowerShapeGeometry;

// Qualification reader only. Loads an existing Native assembly for its getter context.
// No command/setter, execution lease, journal transition or fence-removal capability.
internal static class ReadSolidScaleMembership {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static readonly BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 static Assembly native;
 static void Need(bool v,string error){if(!v)throw new InvalidDataException(error);}
 static object Static(string type,string method,params object[] args){return native.GetType("PowerShapeBridge."+type,true).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
 static object Getter(object context,string method,params object[] args){
  Need(new[]{"Observation","Windows","ConfirmDocument","Composite","Bounds","ClosestSolidFace","FaceNear","FacePoint"}.Contains(method),"Getter not allowed.");
  return context.GetType().GetMethod(method,Instance).Invoke(context,args);
 }
 static Dictionary<string,object> Graph(object value){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(value));}
 static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
 static object[] Values(object value){Need(value is IEnumerable&&!(value is string),"Explicit sequence required.");return ((IEnumerable)value).Cast<object>().ToArray();}
 static double Number(object value){Need(value is int||value is long||value is double||value is float||value is decimal,"Explicit numeric field required.");double v=Convert.ToDouble(value,CultureInfo.InvariantCulture);Need(!double.IsNaN(v)&&!double.IsInfinity(v),"Finite numeric field required.");return v;}
 static double[] Vector(object value){var a=Values(value);Need(a.Length==3,"Explicit three-vector required.");return a.Select(Number).ToArray();}
 static string Digest(object value){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(Json.Serialize(value)))).Replace("-","").ToLowerInvariant();}
 static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,object> Body(Dictionary<string,object> observation){var a=Values(observation["entities"]);Need(a.Length==1&&Convert.ToInt32(observation["entityCount"])==1,"Exactly one observed entity required.");var b=Graph(a[0]);Need(Convert.ToInt32(b["nativeTypeCode"])==40,"SOLID required.");return b;}
 static string Context(Dictionary<string,object> o){
  return Digest(new[]{"processId","processStartedUtc","windowsSessionId","model","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw","workplanes","levelsContainingEntities"}.Select(k=>o[k]).ToArray());
 }
 static string Metadata(Dictionary<string,object> b){return Digest(new[]{"nativeTypeCode","nativeId","name","type","description","levelNumber"}.Select(k=>b[k]).ToArray());}
 static SolidScaleMembershipVerifier.Reading Reading(Dictionary<string,object> observation,string witnessSha,double[] bounds,SolidScaleMembershipVerifier.Witness[] witnesses){
  var body=Body(observation);var g=Graph(body["geometry"]);Need(Equals(g["coordinateFrame"],"world"),"WORLD geometry required.");
  var invariants=new[]{"surfaces","nlinks_open","closedRaw","connectedRaw","watertightRaw","trimming_validRaw"}.Select(k=>{double v=Number(g[k]);Need(v==Math.Truncate(v)&&v<=int.MaxValue&&v>=0,"Integral flags/counts required.");return (int)v;}).ToArray();
  return new SolidScaleMembershipVerifier.Reading(Context(observation),Metadata(body),witnessSha,Convert.ToInt32(body["nativeTypeCode"]),Convert.ToInt32(body["nativeId"]),"world",(string)observation["lengthUnitsRaw"],Number(observation["lengthUnitFactorRaw"]),Number(observation["generalToleranceRaw"]),Digest(new[]{observation["areaUnitNameRaw"],observation["areaUnitFactorRaw"]}),Digest(new[]{observation["volumeUnitNameRaw"],observation["volumeUnitFactorRaw"]}),Number(g["areaRaw"]),Number(g["volumeRaw"]),bounds,Vector(g["cogRaw"]),invariants,witnesses);
 }
 [STAThread]static int Main(string[] args){
  if(args.Length!=4||File.Exists(args[3]))return 2;
  string specPath=Path.GetFullPath(args[0]),experiment=Path.GetFullPath(args[1]),refusal=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);
  object context=null;var report=new Dictionary<string,object>{{"state","solid_scale_membership_read_rejected"},{"cadCommands",0},{"selectionChanges",0},{"executionAuthority",false},{"fenceReleased",false},{"fullTopologyEquivalent",false},{"persistentFaceUvCorrespondenceClaimed",false},{"normalsPreservedClaimed",false},{"profile",SolidScaleMembershipVerifier.Profile}};
  try{
   var spec=Read(specPath);string root=Path.GetFullPath((string)spec["runtimeRoot"]),assembly=Path.GetFullPath((string)spec["assemblyPath"]);
   Need(Hash(assembly)==(string)spec["assemblySha256"],"Native assembly changed.");Need(Hash((string)spec["sourcePath"])==(string)spec["sourceSha256"],"Protected source changed.");
   string config=Path.Combine(root,"bridge.config.json");Need(Hash(config)==(string)spec["configurationSha256"],"Configuration changed.");
   Need(Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.config.json"))==(string)spec["configurationSha256"],"Diagnostic getter configuration must match pinned runtime configuration.");
   string copy=(string)spec["copyId"],folder=Path.Combine(root,"workingcopies",copy),fence=Path.Combine(folder,"mutation-fence.json");
   Need(Hash(Path.Combine(folder,"ownership.json"))==(string)spec["ownershipSha256"],"Ownership receipt changed.");
   var recovery=Graph(spec["resumeAfterKnownSelection"]);string fenceSha=Hash(fence);Need(fenceSha==(string)recovery["originalFenceSha256"],"Original research fence changed.");
   var oldRefusal=Read(refusal);Need(!Equals(Graph(oldRefusal["initialStrictProfile"])["passed"],true),"Original strict refusal must be retained.");report["strictRefusalSha256"]=Hash(refusal);
   var original=Read(Path.Combine(experiment,"before.json"));Need(Equals(original["state"],"succeeded_verified")&&Equals(original["lengthUnitsRaw"],"mm")&&Json.Serialize(original["contextBefore"])=="[1,0]"&&Json.Serialize(original["contextAfter"])=="[1,0]","Recorded single-selection WORLD source reading required.");
   string ownedPath=Path.GetFullPath((string)spec["workingPath"]);var owner=Read(Path.Combine(folder,"ownership.json"));
   Need(string.Equals(Path.GetFullPath((string)Graph(original["model"])["path"]),ownedPath,StringComparison.OrdinalIgnoreCase)&&string.Equals(Path.GetFullPath((string)Graph(owner["working"])["path"]),ownedPath,StringComparison.OrdinalIgnoreCase),"Recorded document must be the fixed owned working path.");
   Need(Hash(Path.Combine(folder,"checkpoint","Before.psmodel"))==(string)spec["sourceSha256"]&&Hash(ownedPath)==(string)spec["sourceSha256"],"Recorded experiment checkpoint/saved working bytes changed.");
   var sourceBody=Body(original);Need(Convert.ToInt32(sourceBody["nativeId"])==Convert.ToInt32(spec["nativeId"])&&Equals(sourceBody["name"],spec["solidName"]),"Recorded source target differs.");
   var sel=Graph(original["selectionBounds"]);Need(Equals(sel["frame"],"world")&&Equals(sel["lengthUnitsRaw"],"mm")&&Equals(sel["scope"],"current_selection_only"),"Recorded source WORLD bounds required.");
   var min=Vector(sel["min"]);var max=Vector(sel["max"]);var beforeBounds=Enumerable.Range(0,6).Select(i=>(i%2==0?min:max)[i/2]).ToArray();
   string sourceFile=Path.Combine(experiment,"source-samples.json"),sourceSha=Hash(sourceFile);var source=Values(Json.DeserializeObject(File.ReadAllText(sourceFile))).Select(Graph).ToArray();Need(source.Length==8,"Eight source samples required.");
   double factor=Number(spec["factor"]);double[] centre=Vector(spec["centre"]);
   native=Assembly.LoadFrom(assembly);var parsed=Static("Program","ParseConfig",File.ReadAllText(config));context=Activator.CreateInstance(native.GetType("PowerShapeBridge.DocumentBindings+ComContext",true),Instance,null,new[]{parsed},null);
   var start=Graph(Getter(context,"Observation"));Need(Equals(start["state"],"succeeded_verified")&&Json.Serialize(start["contextBefore"])=="[0,0]"&&Json.Serialize(start["contextAfter"])=="[0,0]","Fresh verified WORLD/empty reading required.");
   Need(Equals(start["busy"],false)&&Equals(original["busy"],false),"Idle recorded and current context required.");
   Need(Context(original)==Context(start)&&Metadata(sourceBody)==Metadata(Body(start)),"Retained source context/target changed.");
   Need((string)Static("DocumentBindings","WindowDigest",Getter(context,"Windows"))==(string)spec["windowContextSha256"],"Windows changed.");
   int id=Convert.ToInt32(spec["nativeId"]),composite=Convert.ToInt32(Getter(context,"Composite",40,id));var bounds=Values(Getter(context,"Bounds",composite)).Select(Number).ToArray();
   var beforeWitnesses=new List<SolidScaleMembershipVerifier.Witness>();var afterWitnesses=new List<SolidScaleMembershipVerifier.Witness>();var rows=new List<object>();
   for(int i=0;i<8;i++){
    var row=source[i];Need(Convert.ToInt32(row["sample"])==i,"Source witness order changed.");var p=Vector(row["sourcePosition"]);var target=Enumerable.Range(0,3).Select(j=>centre[j]+factor*(p[j]-centre[j])).ToArray();
    Getter(context,"ConfirmDocument");string face=(string)Static("SolidFaceMeasurements","FaceName",Getter(context,"ClosestSolidFace",id,target));var uv=Vector(Getter(context,"FaceNear",face,target));var observed=Vector(Getter(context,"FacePoint",face,uv[0],uv[1],false));
    beforeWitnesses.Add(new SolidScaleMembershipVerifier.Witness("corner-"+i,(string)row["sourceFace"],p));afterWitnesses.Add(new SolidScaleMembershipVerifier.Witness("corner-"+i,face,observed));
    rows.Add(new{sample=i,sourceFace=row["sourceFace"],sourcePosition=p,expectedPosition=target,observedTargetFace=face,observedTargetUV=uv,observedPosition=observed});
   }
   var finish=Graph(Getter(context,"Observation"));Need(Equals(Static("EntitySets","ObservedDigest",start),Static("EntitySets","ObservedDigest",finish))&&(string)Static("DocumentBindings","WindowDigest",Getter(context,"Windows"))==(string)spec["windowContextSha256"]&&composite==Convert.ToInt32(Getter(context,"Composite",40,id))&&bounds.SequenceEqual(Values(Getter(context,"Bounds",composite)).Select(Number)),"Geometry/context changed while measuring.");
   Need(Equals(finish["busy"],false),"Final context busy.");Getter(context,"ConfirmDocument");var result=SolidScaleMembershipVerifier.Verify(Reading(original,sourceSha,beforeBounds,beforeWitnesses.ToArray()),Reading(finish,sourceSha,bounds,afterWitnesses.ToArray()),factor,centre);
   Need(Hash(fence)==fenceSha&&Hash((string)spec["sourcePath"])==(string)spec["sourceSha256"],"Protected fence/source changed.");
   report["state"]="solid_scale_sampled_membership_document_tolerance_verified_observed";report["membershipBudgetMm"]=result.MembershipBudgetMm;report["maximumMembershipCoordinateErrorMm"]=result.MaximumMembershipCoordinateErrorMm;report["maximumMembershipDistanceMm"]=result.MaximumMembershipDistanceMm;report["cogCoordinateErrorMm"]=result.CogCoordinateErrorMm;report["boundsCoordinateErrorMm"]=result.BoundsCoordinateErrorMm;report["areaErrorRaw"]=result.AreaErrorRaw;report["volumeErrorRaw"]=result.VolumeErrorRaw;report["changedObservedFaceNames"]=result.ChangedObservedFaceNames;report["beforeRead"]=start;report["afterRead"]=finish;report["beforeBoundsWorldMm"]=beforeBounds;report["boundsWorldMm"]=bounds;report["witnesses"]=rows;report["sourceWitnessSha256"]=sourceSha;report["areaUnitObservationsRaw"]=new[]{finish["areaUnitNameRaw"],finish["areaUnitFactorRaw"]};report["volumeUnitObservationsRaw"]=new[]{finish["volumeUnitNameRaw"],finish["volumeUnitFactorRaw"]};report["rawMetricsConvertedToSI"]=false;report["originalFenceSha256"]=fenceSha;report["sourceCheckpointSha256"]=Hash(Path.Combine(folder,"checkpoint","Before.psmodel"));report["savedHashes"]=Static("WorkingCopyOpen","SavedHashes",Getter(context,"Windows"));report["publicMcpScaleProviderQualified"]=false;return 0;
  }catch(Exception error){report["error"]=error.ToString();return 1;}
  finally{report["timestampUtc"]=DateTime.UtcNow.ToString("o");report["readerSha256"]=Hash(Assembly.GetExecutingAssembly().Location);report["scenarioSha256"]=Hash(specPath);using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var writer=new StreamWriter(file))writer.Write(Json.Serialize(report));if(context!=null)((IDisposable)context).Dispose();}
 }
}
