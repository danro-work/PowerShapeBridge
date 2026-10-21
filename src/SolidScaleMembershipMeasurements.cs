using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Getter-only Native measurement adapter. Its caller must hold ownership and
 // checkpoint proofs. No command, queue route, journal or fence-release authority.
 internal static class SolidScaleMembershipMeasurements {
  internal const string Qualification="Sampled target-surface membership only; no persistent face/UV correspondence, normals or full topology equivalence";
  internal sealed class Measurement {
   internal readonly SolidScaleMembershipVerifier.Reading Before,After;
   internal readonly SolidScaleMembershipVerifier.Result Verification;
   internal readonly Dictionary<string,object> Observation;
   internal readonly Dictionary<string,object>[] WitnessDetails;
   internal Measurement(SolidScaleMembershipVerifier.Reading before,SolidScaleMembershipVerifier.Reading after,SolidScaleMembershipVerifier.Result verification,Dictionary<string,object> observation,Dictionary<string,object>[] details){Before=before;After=after;Verification=verification;Observation=Clone(observation);WitnessDetails=details.Select(Clone).ToArray();}
  }
  static void Need(bool value,string why){if(!value)throw new InvalidDataException(why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static string Digest(object value){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(EntitySets.Canonical(value)))).Replace("-","").ToLowerInvariant();}
  static void Fields(Dictionary<string,object> d,params string[] fields){Need(d!=null&&d.Count==fields.Length&&fields.All(d.ContainsKey),"Scale measurement fields differ.");}
  static object[] Values(object value){var a=value as IList;Need(a!=null&&(!(value is Array)||((Array)value).Rank==1),"One-dimensional explicit scale measurement sequence required.");return a.Cast<object>().ToArray();}
  static double Number(object value){Need(value is int||value is long||value is double||value is float||value is decimal,"Numeric scale measurement required.");double n=Program.StrictDouble(value);Need(!double.IsNaN(n)&&!double.IsInfinity(n),"Finite scale measurement required.");return n;}
  static double[] Numbers(object value){return Values(value).Select(Number).ToArray();}
  static Dictionary<string,object> Body(Dictionary<string,object> o){
   Need(Equals(o["state"],"succeeded_verified")&&Equals(o["busy"],false)&&Equals(o["entityCount"],1),"Idle verified one-entity observation required.");
   foreach(string key in new[]{"contextBefore","contextAfter"})Need(EntitySets.Canonical(o[key])=="[0,0]","WORLD and empty context required.");
   foreach(string key in new[]{"selectionBefore","selectionAfter"})Need(EntitySets.Canonical(o[key])=="[]","Empty selection required.");
   var entities=EntitySets.Inventory(o);Need(entities.Length==1&&Equals(entities[0]["nativeTypeCode"],40)&&Program.StrictInt(entities[0]["nativeId"])>0,"Exactly one positive SOLID tuple required.");
   var body=entities[0];Need(body["name"] is string&&!string.IsNullOrWhiteSpace((string)body["name"])&&Equals(body["type"],"SOLID")&&body["description"] is string&&body["levelNumber"] is int,"Complete SOLID metadata required.");return body;
  }
  static string Context(Dictionary<string,object> o,string windows){
   Need(EntitySets.Hex(windows,64),"Pinned Native window digest required.");
   return Digest(new object[]{new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw","workplanes","levelsContainingEntities"}.Select(k=>o[k]).ToArray(),windows});
  }
  static string Metadata(Dictionary<string,object> body){return Digest(new[]{"nativeTypeCode","nativeId","name","type","description","levelNumber"}.Select(k=>body[k]).ToArray());}
  static SolidScaleMembershipVerifier.Reading Reading(Dictionary<string,object> observation,string windows,string sourceWitness,double[] bounds,SolidScaleMembershipVerifier.Witness[] witnesses){
   var b=Body(observation);var g=b["geometry"] as Dictionary<string,object>;Need(g!=null&&Equals(g["coordinateFrame"],"world"),"WORLD SOLID geometry required.");
   int[] flags=new[]{"surfaces","nlinks_open","closedRaw","connectedRaw","watertightRaw","trimming_validRaw"}.Select(k=>{double n=Number(g[k]);Need(n==Math.Truncate(n)&&n>=0&&n<=int.MaxValue,"Integral Native SOLID counts/flags required.");return (int)n;}).ToArray();
   Need(observation["lengthUnitsRaw"] is string,"Explicit length units required.");
   var reading=new SolidScaleMembershipVerifier.Reading(Context(observation,windows),Metadata(b),sourceWitness,40,Program.StrictInt(b["nativeId"]),"world",(string)observation["lengthUnitsRaw"],Number(observation["lengthUnitFactorRaw"]),Number(observation["generalToleranceRaw"]),Digest(new[]{observation["areaUnitNameRaw"],observation["areaUnitFactorRaw"]}),Digest(new[]{observation["volumeUnitNameRaw"],observation["volumeUnitFactorRaw"]}),Number(g["areaRaw"]),Number(g["volumeRaw"]),bounds,SolidFaceMeasurements.Vector(g["cogRaw"]),flags,witnesses);
   SolidScaleMembershipVerifier.Validate(reading);return reading;
  }
  static double[] Corner(double[] bounds,int mask){Need(bounds!=null&&bounds.Length==6,"Paired WORLD bounds required.");return Enumerable.Range(0,3).Select(j=>bounds[2*j+((mask&(1<<j))==0?0:1)]).ToArray();}
  static SolidScaleMembershipVerifier.Witness[] PreflightCorners(double[] bounds){return Enumerable.Range(0,8).Select(i=>new SolidScaleMembershipVerifier.Witness("corner-"+i,"preflight",Corner(bounds,i))).ToArray();}
  static Dictionary<string,object> Observe(SolidFaceMeasurements.IContext c){c.ConfirmDocument();var o=Clone(c.Observation());Body(o);return o;}
  static void SameStart(Dictionary<string,object> expected,Dictionary<string,object> actual){Need(EntitySets.ObservedDigest(expected)==EntitySets.ObservedDigest(actual),"Source observation changed before measuring.");}
  static void Stable(SolidFaceMeasurements.IContext c,Dictionary<string,object> start,int nativeId,int composite,double[] bounds,string windows){
   var end=Observe(c);Need(EntitySets.ObservedDigest(start)==EntitySets.ObservedDigest(end)&&DocumentBindings.WindowDigest(c.Windows())==windows&&c.Composite(40,nativeId)==composite&&bounds.SequenceEqual(c.Bounds(composite)),"Document/geometry/windows/mapping changed while measuring scale membership.");c.ConfirmDocument();
  }
  static Dictionary<string,object> Sample(SolidFaceMeasurements.IContext c,int nativeId,int index,double[] query,out Dictionary<string,object> detail){
   c.ConfirmDocument();string face=SolidFaceMeasurements.FaceName(c.ClosestSolidFace(nativeId,query));var uv=SolidFaceMeasurements.Vector(c.FaceNear(face,query));var p=SolidFaceMeasurements.Vector(c.FacePoint(face,uv[0],uv[1],false));
   detail=new Dictionary<string,object>{{"key","corner-"+index},{"queryWorldMm",query.ToArray()},{"observedFace",face},{"observedUV",uv},{"observedWorldMm",p.ToArray()}};
   return new Dictionary<string,object>{{"key","corner-"+index},{"faceObserved",face},{"positionWorldMm",p}};
  }
  static SolidScaleMembershipVerifier.Witness[] Witnesses(object raw){
   var rows=Values(raw);Need(rows.Length==8,"Eight source XYZ witnesses required.");return rows.Select((v,i)=>{var row=v as Dictionary<string,object>;Fields(row,"key","faceObserved","positionWorldMm");Need(Equals(row["key"],"corner-"+i)&&row["faceObserved"] is string,"Ordered source witness key/face required.");SolidFaceMeasurements.FaceName(row["faceObserved"]);return new SolidScaleMembershipVerifier.Witness((string)row["key"],(string)row["faceObserved"],SolidFaceMeasurements.Vector(row["positionWorldMm"]));}).ToArray();
  }
  internal static Dictionary<string,object> CaptureSource(Dictionary<string,object> expected,int nativeId,string windowContextSha256,SolidFaceMeasurements.IContext c){
   expected=Clone(expected);var b=Body(expected);Need(Equals(b["nativeId"],nativeId),"Source target differs.");
   var start=Observe(c);SameStart(expected,start);Need(DocumentBindings.WindowDigest(c.Windows())==windowContextSha256,"Source windows differ.");
   int composite=c.Composite(40,nativeId);Need(composite>0,"Positive explicit SOLID mapping required.");var bounds=c.Bounds(composite).ToArray();
   // Reject unsupported units/health/tolerance before querying any face.
   Reading(start,windowContextSha256,new string('0',64),bounds,PreflightCorners(bounds));var rows=new List<Dictionary<string,object>>();
   for(int i=0;i<8;i++){Dictionary<string,object> detail;rows.Add(Sample(c,nativeId,i,Corner(bounds,i),out detail));}
   var bundle=new Dictionary<string,object>{{"schemaVersion",1},{"profile",SolidScaleMembershipVerifier.Profile},{"entityKey",new[]{40,nativeId}},{"sourceCompositeId",composite},{"sourceObservationSha256",EntitySets.ObservedDigest(expected)},{"windowContextSha256",windowContextSha256},{"sourceBoundsWorldMmPaired",bounds},{"sourceWitnesses",rows},{"sourceWitnessSha256",Digest(rows)},{"qualification",Qualification}};
   Source(bundle,expected);Stable(c,start,nativeId,composite,bounds,windowContextSha256);return bundle;
  }
  internal static SolidScaleMembershipVerifier.Reading Source(Dictionary<string,object> bundle,Dictionary<string,object> expected){
   Fields(bundle,"schemaVersion","profile","entityKey","sourceCompositeId","sourceObservationSha256","windowContextSha256","sourceBoundsWorldMmPaired","sourceWitnesses","sourceWitnessSha256","qualification");
   Need(Equals(bundle["schemaVersion"],1)&&Equals(bundle["profile"],SolidScaleMembershipVerifier.Profile)&&Equals(bundle["qualification"],Qualification),"Unsupported scale measurement profile.");
   var b=Body(expected);Need(EntitySets.Canonical(bundle["entityKey"])==EntitySets.Canonical(new[]{40,Program.StrictInt(b["nativeId"])}),"Source SOLID key differs.");
   Need(bundle["sourceCompositeId"] is int&&Program.StrictInt(bundle["sourceCompositeId"])>0,"Pinned positive source composite mapping required.");
   Need(Equals(bundle["sourceObservationSha256"],EntitySets.ObservedDigest(expected))&&Equals(bundle["sourceWitnessSha256"],Digest(bundle["sourceWitnesses"])),"Source observation/XYZ digest differs.");
   Need(bundle["windowContextSha256"] is string&&bundle["sourceWitnessSha256"] is string,"Pinned source digest text required.");
   return Reading(expected,(string)bundle["windowContextSha256"],(string)bundle["sourceWitnessSha256"],Numbers(bundle["sourceBoundsWorldMmPaired"]),Witnesses(bundle["sourceWitnesses"]));
  }
  internal static double[] CentreAndCheck(SolidScaleMembershipVerifier.Reading source,double factor,double[] centre){
   Need(!double.IsNaN(factor)&&!double.IsInfinity(factor)&&factor>=0.01&&factor<=100&&factor!=1,"Bounded positive non-noop factor required.");
   var p=SolidFaceMeasurements.Vector(centre);Need(p.All(v=>Math.Abs(v)<=1000000),"Bounded explicit WORLD centre required.");
   Need(Enumerable.Range(0,6).All(i=>Math.Abs(p[i/2]+factor*(source.Bounds[i]-p[i/2]))<=1000000),"Transformed bounds outside qualified domain.");
   Need(source.Witnesses.Max(w=>Enumerable.Range(0,3).Max(i=>Math.Abs((factor-1)*(w.Position[i]-p[i]))))>2*source.DocumentToleranceMm,"Requested scale is indistinguishable at document tolerance.");return p;
  }
  internal static Measurement MeasureTarget(Dictionary<string,object> sourceBundle,Dictionary<string,object> expected,double factor,double[] centre,SolidFaceMeasurements.IContext c){
   // Detach before callbacks: getters cannot change accepted source parameters.
   sourceBundle=Clone(sourceBundle);expected=Clone(expected);var before=Source(sourceBundle,expected);centre=CentreAndCheck(before,factor,centre);
   string windows=(string)sourceBundle["windowContextSha256"];var start=Observe(c);Need(Context(start,windows)==before.ContextSha256&&Metadata(Body(start))==before.MetadataSha256,"Retained source process/document/metadata/units changed.");
   Need(DocumentBindings.WindowDigest(c.Windows())==windows,"Target windows changed.");int composite=c.Composite(40,before.NativeId);Need(Equals(sourceBundle["sourceCompositeId"],composite),"Pinned explicit SOLID mapping changed.");var bounds=c.Bounds(composite).ToArray();
   Reading(start,windows,before.SourceWitnessSha256,bounds,PreflightCorners(bounds));var rows=new List<Dictionary<string,object>>();var details=new List<Dictionary<string,object>>();
   for(int i=0;i<8;i++){var p=Enumerable.Range(0,3).Select(j=>centre[j]+factor*(before.Witnesses[i].Position[j]-centre[j])).ToArray();Dictionary<string,object> detail;rows.Add(Sample(c,before.NativeId,i,p,out detail));details.Add(detail);}
   Stable(c,start,before.NativeId,composite,bounds,windows);
   var after=Reading(start,windows,before.SourceWitnessSha256,bounds,Witnesses(rows));var verified=SolidScaleMembershipVerifier.Verify(before,after,factor,centre);return new Measurement(before,after,verified,start,details.ToArray());
  }
 }
}
