using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Passive witnesses on a SOLID. Face names and UVs are captured BEFORE rotation,
 // then reused verbatim. Recomputing CLOSEST_FACE/NEAR after rotation is forbidden.
 // This samples faces; it never promises complete B-rep/topology equivalence.
 internal static class SolidFaceMeasurements {
  internal interface IContext : DocumentBindings.IContext {
   int Composite(int type,int nativeId);
   double[] Bounds(int composite);
   object ClosestSolidFace(int nativeId,double[] point);
   object FaceNear(string face,double[] point);
   object FacePoint(string face,double t,double u,bool normal);
  }
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  internal static double[] Vector(object raw) {
   var sequence=raw as IEnumerable;Need(sequence!=null&&!(raw is string)&&(!(raw is Array)||((Array)raw).Rank==1),"One-dimensional native vector array required.");
   var values=sequence.Cast<object>().ToArray();Need(values.Length==3&&values.All(v=>v is int||v is long||v is float||v is double||v is decimal),"Three numeric native components required.");
   var result=values.Select(Program.StrictDouble).ToArray();Need(result.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)),"Finite native vector required.");return result;
  }
  internal static string FaceName(object raw) {
   Need(raw is string||raw is int||raw is long||raw is double||raw is decimal,"Documented face name must be text or numeric integer.");
   if(!(raw is string)){double number=Program.StrictDouble(raw);Need(number>=0&&number<=int.MaxValue&&number==Math.Truncate(number),"Native numeric face name must be a finite nonnegative integer.");}
   string name=Convert.ToString(raw,CultureInfo.InvariantCulture);
   Need(!string.IsNullOrWhiteSpace(name)&&name.Length<=512&&name!="Hello PowerShape User"&&!name.Any(c=>c=='\''||c=='\"'||c=='['||c==']'||char.IsControl(c)),"Invalid/unavailable face name; no guessed fallback.");return name;
  }
  internal static string Coordinates(double[] p){Vector(p);return string.Join(";",p.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));}
  static Dictionary<string,object> Body(Dictionary<string,object> o,int id){var matches=EntitySets.Inventory(o).Where(e=>Equals(e["nativeTypeCode"],40)&&Equals(e["nativeId"],id)).ToArray();Need(matches.Length==1,"Explicit unique SOLID required.");return matches[0];}
  // Passive getters preserve existing selection. The executor's separate held
  // ownership/rotation-plan guards still require empty selection before mutation.
  static void World(Dictionary<string,object> o){
   var context=((IEnumerable)o["contextBefore"]).Cast<object>().ToArray();
   Need(context.Length==2&&Equals(context[1],0)&&EntitySets.Canonical(o["contextBefore"])==EntitySets.Canonical(o["contextAfter"])&&EntitySets.Canonical(o["selectionBefore"])==EntitySets.Canonical(o["selectionAfter"]),"Stable WORLD/selection context required for passive witnesses.");
  }
  static void Box(double[] b){Need(b!=null&&b.Length==6&&b.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v))&&Enumerable.Range(0,3).All(i=>b[2*i]<b[2*i+1]),"Nondegenerate finite paired bounds required.");}
  static string Digest(object value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(EntitySets.Canonical(value)))).Replace("-","").ToLowerInvariant();}
  static void Fields(Dictionary<string,object> d,params string[] fields){Need(d!=null&&d.Count==fields.Length&&fields.All(d.ContainsKey),"Solid witness descriptor fields differ.");}
  static Dictionary<string,object>[] Anchors(Dictionary<string,object> descriptor) {
   Fields(descriptor,"schemaVersion","entityKey","frame","units","lengthUnitsRaw","lengthUnitFactorRaw","anchors","qualification");
   Need(Equals(descriptor["schemaVersion"],1)&&Equals(descriptor["frame"],"world")&&Equals(descriptor["units"],"document_raw"),"Explicit WORLD descriptor required.");
   var keys=EntitySets.Keys(new[]{descriptor["entityKey"]});Need(keys.Length==1&&keys[0][0]==40,"Solid key required.");
   var rows=((IEnumerable)descriptor["anchors"]).Cast<object>().Select(v=>v as Dictionary<string,object>).ToArray();Need(rows.Length==8,"Eight pinned witnesses required.");
   for(int i=0;i<rows.Length;i++){
    var row=rows[i];Fields(row,"anchor","faceName","t","u");Need(Equals(row["anchor"],"corner-"+i)&&row["faceName"] is string,"Stable ordered witness anchor/name required.");FaceName(row["faceName"]);
    foreach(string k in new[]{"t","u"})Need(row[k] is int||row[k] is long||row[k] is double||row[k] is float||row[k] is decimal,"Numeric UV required.");Vector(new object[]{row["t"],row["u"],0});
   }
   return rows;
  }
  internal static Dictionary<string,object> Capture(Dictionary<string,object> expected,int id,IContext c) {
   World(expected);Body(expected,id);c.ConfirmDocument();int composite=c.Composite(40,id);var bounds=c.Bounds(composite);Box(bounds);string windows=DocumentBindings.WindowDigest(c.Windows());
   var rows=new List<Dictionary<string,object>>();
   for(int mask=0;mask<8;mask++){
    var p=Enumerable.Range(0,3).Select(i=>bounds[2*i+((mask&(1<<i))==0?0:1)]).ToArray();c.ConfirmDocument();
    string face=FaceName(c.ClosestSolidFace(id,p));var tu=Vector(c.FaceNear(face,p));
    rows.Add(new Dictionary<string,object>{{"anchor","corner-"+mask},{"faceName",face},{"t",tu[0]},{"u",tu[1]}});
   }
   var descriptor=new Dictionary<string,object>{{"schemaVersion",1},{"entityKey",new[]{40,id}},{"frame","world"},{"units","document_raw"},{"lengthUnitsRaw",expected["lengthUnitsRaw"]},{"lengthUnitFactorRaw",expected["lengthUnitFactorRaw"]},{"anchors",rows},{"qualification","Pinned closest-face UV samples, normals, COG, raw metrics and WORLD bounds; sampled observations only, not full topology"}};
   Anchors(descriptor);Need(EntitySets.ObservedDigest(expected)==EntitySets.ObservedDigest(c.Observation())&&windows==DocumentBindings.WindowDigest(c.Windows())&&composite==c.Composite(40,id)&&bounds.SequenceEqual(c.Bounds(composite)),"Context/geometry changed while capturing solid witnesses.");c.ConfirmDocument();return descriptor;
  }
  internal static RotationSampleVerifier.Samples Read(Dictionary<string,object> descriptor,IContext c) {
   var anchors=Anchors(descriptor);int id=EntitySets.Keys(new[]{descriptor["entityKey"]})[0][1];c.ConfirmDocument();var before=c.Observation();World(before);
   Need(EntitySets.Canonical(before["lengthUnitsRaw"])==EntitySets.Canonical(descriptor["lengthUnitsRaw"])&&EntitySets.Canonical(before["lengthUnitFactorRaw"])==EntitySets.Canonical(descriptor["lengthUnitFactorRaw"]),"Descriptor length units changed.");
   var body=Body(before,id);var geometry=body["geometry"] as Dictionary<string,object>;Need(geometry!=null&&Equals(geometry["coordinateFrame"],"world"),"WORLD solid metrics required.");
   double tolerance=Program.StrictDouble(before["generalToleranceRaw"]);Need(tolerance>0&&tolerance<=1&&!double.IsNaN(tolerance)&&!double.IsInfinity(tolerance),"Explicit finite bounded document raw tolerance required.");
   int composite=c.Composite(40,id);double[] bounds=c.Bounds(composite);Box(bounds);string windows=DocumentBindings.WindowDigest(c.Windows());
   var positions=new List<RotationSampleVerifier.Witness>();var normals=new List<RotationSampleVerifier.Witness>();
   foreach(var a in anchors){
    c.ConfirmDocument();string face=(string)a["faceName"];double t=Program.StrictDouble(a["t"]),u=Program.StrictDouble(a["u"]);
    var p=Vector(c.FacePoint(face,t,u,false));var n=Vector(c.FacePoint(face,t,u,true));Need(Math.Abs(n.Sum(v=>v*v)-1)<=5e-6,"Nonunit/unavailable face normal.");
    Need(Enumerable.Range(0,3).All(i=>p[i]>=bounds[2*i]-2*tolerance&&p[i]<=bounds[2*i+1]+2*tolerance),"Face sample lies outside target WORLD bounds; mapping is not accepted.");
    positions.Add(new RotationSampleVerifier.Witness((string)a["anchor"],p));normals.Add(new RotationSampleVerifier.Witness((string)a["anchor"]+"-normal",n));
   }
   // No COG-only fallback: require spatially distinct face witnesses as well.
   Need(positions.Select(p=>Coordinates(p.Value)).Distinct().Count()>=4,"Insufficient spatially distinct face witnesses.");
   positions.Add(new RotationSampleVerifier.Witness("solid-cog",Vector(geometry["cogRaw"])));
   string[] metrics={"volumeRaw","areaRaw","surfaces","nlinks_open","closedRaw","connectedRaw","watertightRaw","trimming_validRaw"};
   var invariants=metrics.Select(k=>{Need(geometry.ContainsKey(k),"Missing rigid solid invariant: "+k);return Program.StrictDouble(geometry[k]);}).ToArray();
   var after=c.Observation();Need(EntitySets.ObservedDigest(before)==EntitySets.ObservedDigest(after)&&windows==DocumentBindings.WindowDigest(c.Windows())&&composite==c.Composite(40,id)&&bounds.SequenceEqual(c.Bounds(composite)),"Context/geometry changed while reading solid witnesses.");c.ConfirmDocument();
   var samples=new RotationSampleVerifier.Samples(40,id,"world","document_raw",Digest(descriptor),positions.ToArray(),normals.ToArray(),invariants,bounds);RotationSampleVerifier.Validate(samples);return samples;
  }
 }
}
