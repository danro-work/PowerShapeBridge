using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Passive documented getter adapter. No selection/activation/Exec/save/rotation callback.
 internal static class RotationMeasurements {
  internal interface IContext : DocumentBindings.IContext {
   int Composite(int type,int nativeId);
   double[] Bounds(int composite);
   object SurfacePoints(string name,double tStart,double tEnd,int tCount,double uStart,double uEnd,int uCount);
   object CurvePoints(string name,int start,int end);
  }
  internal sealed class Reading {
   internal readonly RotationSampleVerifier.Samples Samples;internal readonly Dictionary<string,object> Descriptor,Raw;
   internal Reading(RotationSampleVerifier.Samples samples,Dictionary<string,object> descriptor,Dictionary<string,object> raw){Samples=samples;Descriptor=descriptor;Raw=raw;}
  }
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static double Number(object value){Require(value is int||value is long||value is float||value is double||value is decimal,"Native numeric field required.");double number=Program.StrictDouble(value);Require(!double.IsNaN(number)&&!double.IsInfinity(number),"Native nonfinite measurement.");return number;}
  static object[] Sequence(object value){var array=value as Array;if(array!=null){Require(array.Rank==1,"One-dimensional native array required.");return array.Cast<object>().ToArray();}var list=value as IList;Require(list!=null&&!(value is string),"Native array required.");return list.Cast<object>().ToArray();}
  static double[] Numbers(object value,int count){var values=Sequence(value);Require(values.Length==count,"Native numeric array shape differs.");return values.Select(Number).ToArray();}
  static bool[] Flags(object value,int count){var values=Sequence(value);Require(values.Length==count&&values.All(v=>v is bool),"Native BOOL array shape/type differs.");return values.Cast<bool>().ToArray();}
  static string Digest(object value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(EntitySets.Canonical(value)))).Replace("-","").ToLowerInvariant();}
  static object[] Response(object raw,int fields){var values=Sequence(raw);Require(values.Length==fields&&values[0] is int&&(int)values[0]==0,"Documented getter success0/response shape required.");return values;}
  static RotationSampleVerifier.Witness[] Points(double[] flat,string prefix){return Enumerable.Range(0,flat.Length/3).Select(i=>new RotationSampleVerifier.Witness(prefix+i,flat.Skip(3*i).Take(3).ToArray())).ToArray();}
  static Dictionary<string,object> Descriptor(Dictionary<string,object> expected,int[] key,string name,string method,object domain){return new Dictionary<string,object>{{"schemaVersion",1},{"entityKey",key},{"name",name},{"method",method},{"domain",domain},{"coordinateFrame","world"},{"lengthUnits","document_raw"},{"lengthUnitsRaw",expected["lengthUnitsRaw"]},{"lengthUnitFactorRaw",expected["lengthUnitFactorRaw"]}};}
  static string Guard(Dictionary<string,object> expected,int[] key,DateTime expires,IContext context,string windows=null) {
   Require(key.Length==2&&new[]{21,59}.Contains(key[0]),"Surface/curve measurement key required; solid face provider not qualified.");
   Require(EntitySets.Canonical(expected["contextBefore"])==EntitySets.Canonical(new[]{0,0})&&EntitySets.Canonical(expected["contextAfter"])==EntitySets.Canonical(new[]{0,0})&&Sequence(expected["selectionBefore"]).Length==0&&Sequence(expected["selectionAfter"]).Length==0,"World/empty-selection read required.");
   EntitySets.CheckObserved(expected,expected,new[]{key},expires,DateTime.UtcNow);context.ConfirmDocument();
   var before=context.Observation();var after=context.Observation();EntitySets.CheckObserved(expected,before,new[]{key},expires,DateTime.UtcNow);EntitySets.CheckObserved(expected,after,new[]{key},expires,DateTime.UtcNow);
   string digest=DocumentBindings.WindowDigest(context.Windows());Require(windows==null||windows==digest,"Measurement window context changed.");context.ConfirmDocument();return digest;
  }
  static string Name(Dictionary<string,object> expected,int[] key){var inventory=EntitySets.Inventory(expected);var entity=inventory.Single(e=>Equals(e["nativeTypeCode"],key[0])&&Equals(e["nativeId"],key[1]));string name=entity["name"] as string;Require(!string.IsNullOrWhiteSpace(name)&&name.Length<=512&&inventory.Count(e=>Equals(e["nativeTypeCode"],key[0])&&Equals(e["name"],name))==1,"Getter name absent/ambiguous; do not guess another entity.");return name;}
  static void Bounds(double[] box){Require(box.Length==6&&box.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v))&&box[0]<=box[1]&&box[2]<=box[3]&&box[4]<=box[5],"Paired native world bounds required.");}
  internal static Reading Surface(Dictionary<string,object> expected,int[] key,DateTime expires,IContext context,double tStart,double tEnd,int tCount,double uStart,double uEnd,int uCount) {
   Require(key.Length==2&&key[0]==21,"Surface measurement required.");Require(new[]{tStart,tEnd,uStart,uEnd}.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v))&&tStart<tEnd&&uStart<uEnd&&tCount>=2&&uCount>=2&&tCount<=256&&uCount<=256,"Explicit finite surface ranges/grid required; no inferred parameter range.");
   string windows=Guard(expected,key,expires,context);string name=Name(expected,key);int composite=context.Composite(key[0],key[1]);var bounds=context.Bounds(composite);Bounds(bounds);
   object response=context.SurfacePoints(name,tStart,tEnd,tCount,uStart,uEnd,uCount);int count=tCount*uCount;var raw=Response(response,4);var positions=Numbers(raw[1],3*count);var normals=Numbers(raw[2],3*count);var visibility=Flags(raw[3],count);
   Guard(expected,key,expires,context,windows);Require(context.Composite(key[0],key[1])==composite,"Surface mapping changed.");var final=context.Bounds(composite);Bounds(final);Require(bounds.SequenceEqual(final),"Bounds changed during measurement read.");
   var domain=new Dictionary<string,object>{{"tStart",tStart},{"tEnd",tEnd},{"tCount",tCount},{"uStart",uStart},{"uEnd",uEnd},{"uCount",uCount},{"visibility",visibility}};var descriptor=Descriptor(expected,key,name,"IPshapeModelDoc.GetSurfacePoints(world_coords=1)",domain);
   var samples=new RotationSampleVerifier.Samples(21,key[1],"world","document_raw",Digest(descriptor),Points(positions,"grid-position-"),Points(normals,"grid-normal-"),new double[0],bounds);
   return new Reading(samples,descriptor,new Dictionary<string,object>{{"getterResponse",response},{"boundsWorldRawPaired",bounds},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"scope","sampled surface grid including visibility; not full topology"}});
  }
  internal static Reading Curve(Dictionary<string,object> expected,int[] key,DateTime expires,IContext context,int start,int end) {
   Require(key.Length==2&&key[0]==59&&start>=0&&end>=start&&(long)end-start+1<=65536,"Explicit bounded inclusive curve indices required; no inferred point count.");
   string windows=Guard(expected,key,expires,context);string name=Name(expected,key);int composite=context.Composite(key[0],key[1]);var bounds=context.Bounds(composite);Bounds(bounds);object response=context.CurvePoints(name,start,end);int count=end-start+1;var raw=Response(response,10);
   var positions=Numbers(raw[1],3*count);var before=Numbers(raw[2],3*count);var magnitudesBefore=Numbers(raw[3],3*count);var directionsBeforeFree=Flags(raw[4],count);var magnitudesBeforeFree=Flags(raw[5],count);var after=Numbers(raw[6],3*count);var magnitudesAfter=Numbers(raw[7],3*count);var directionsAfterFree=Flags(raw[8],count);var magnitudesAfterFree=Flags(raw[9],count);
   Guard(expected,key,expires,context,windows);Require(context.Composite(key[0],key[1])==composite,"Curve mapping changed.");var final=context.Bounds(composite);Bounds(final);Require(bounds.SequenceEqual(final),"Bounds changed during curve measurement read.");
   // The installed HTML documents 3*N magnitude arrays. Other layouts are explicitly unavailable until qualified.
   var domain=new Dictionary<string,object>{{"start",start},{"end",end},{"magnitudeLayout","installed_documentation_3_values_per_point"},{"magnitudesBefore",magnitudesBefore},{"magnitudesAfter",magnitudesAfter},{"directionsBeforeFree",directionsBeforeFree},{"magnitudesBeforeFree",magnitudesBeforeFree},{"directionsAfterFree",directionsAfterFree},{"magnitudesAfterFree",magnitudesAfterFree}};var descriptor=Descriptor(expected,key,name,"IPshapeModelDoc.GetCompCurvePointData(world_coords=1)",domain);
   var samples=new RotationSampleVerifier.Samples(59,key[1],"world","document_raw",Digest(descriptor),Points(positions,"curve-position-"),Points(before,"curve-tangent-before-").Concat(Points(after,"curve-tangent-after-")).ToArray(),new double[0],bounds);
   return new Reading(samples,descriptor,new Dictionary<string,object>{{"getterResponse",response},{"boundsWorldRawPaired",bounds},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"scope","documented curve point/tangent layout; live magnitude layout unqualified"}});
  }
 }
}
