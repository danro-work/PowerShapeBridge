using System;
using System.Linq;

namespace PowerShapeGeometry {
 // Shared Framework/.NET reference predicate. It does not read CAD or assert complete topology.
 internal static class RotationSampleVerifier {
  internal sealed class Witness {
   internal readonly string Key;internal readonly double[] Value;
   internal Witness(string key,double[] value){Key=key;Value=value.ToArray();}
  }
  internal sealed class Samples {
   internal readonly int Type,NativeId;internal readonly string Frame,Units,DescriptorSha256;
   internal readonly Witness[] Positions,Directions;internal readonly double[] Invariants,BoundsPaired;
   internal Samples(int type,int nativeId,string frame,string units,string descriptorSha256,Witness[] positions,Witness[] directions,double[] invariants,double[] boundsPaired) {
    Type=type;NativeId=nativeId;Frame=frame;Units=units;DescriptorSha256=descriptorSha256;
    Positions=positions.Select(p=>new Witness(p.Key,p.Value)).ToArray();Directions=directions.Select(p=>new Witness(p.Key,p.Value)).ToArray();Invariants=invariants.ToArray();BoundsPaired=boundsPaired.ToArray();
   }
  }
  static void Require(bool ok,string error){if(!ok)throw new ArgumentException(error);}
  static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
  static void Vector(double[] value){Require(value.Length==3&&value.All(Finite),"Finite 3D witness required.");}
  static void Box(double[] value){Require(value.Length==6&&value.All(Finite)&&value[0]<=value[1]&&value[2]<=value[3]&&value[4]<=value[5],"Paired world bounds required.");}
  static void Valid(Samples value) {
   Require(new[]{40,21,59}.Contains(value.Type)&&value.NativeId>=0&&value.Frame=="world"&&value.Units=="document_raw","Explicit native key/world/document_raw required.");
   Require(value.DescriptorSha256.Length==64&&value.DescriptorSha256.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"Pinned measurement descriptor SHA required.");
   Require(value.Positions.Length>0&&value.Positions.Length<=65536&&value.Directions.Length<=131072&&value.Invariants.Length<=128&&value.Invariants.All(Finite),"Missing/oversized/invalid samples.");
   foreach(var list in new[]{value.Positions,value.Directions}){Require(list.All(p=>!string.IsNullOrWhiteSpace(p.Key)&&p.Key.Length<=128)&&list.Select(p=>p.Key).Distinct(StringComparer.Ordinal).Count()==list.Length,"Unique immutable witness anchors required.");foreach(var point in list)Vector(point.Value);}
   Box(value.BoundsPaired);
  }
  internal static double[] Transform(double[] point,int angle,double[] axis,bool direction=false) {
   Vector(point);Require(axis.Length==6&&axis.All(Finite),"Explicit point/direction axis required.");
   Require(Math.Abs((long)angle)<=1000000&&angle%360!=0,"Explicit non-noop integer angle required.");
   var unit=axis.Skip(3).ToArray();Require(Math.Abs(unit.Sum(value=>value*value)-1)<=1e-12,"Unit axis required.");
   var origin=direction?new double[3]:axis.Take(3).ToArray();var relative=point.Zip(origin,(p,o)=>p-o).ToArray();var radians=(angle%360)*Math.PI/180;double sine=Math.Sin(radians),cosine=Math.Cos(radians),dot=relative.Zip(unit,(p,d)=>p*d).Sum();
   var cross=new[]{unit[1]*relative[2]-unit[2]*relative[1],unit[2]*relative[0]-unit[0]*relative[2],unit[0]*relative[1]-unit[1]*relative[0]};
   var result=Enumerable.Range(0,3).Select(i=>origin[i]+relative[i]*cosine+cross[i]*sine+unit[i]*dot*(1-cosine)).ToArray();Vector(result);return result;
  }
  internal static double[] Envelope(double[] paired,int angle,double[] axis) {
   Box(paired);var corners=Enumerable.Range(0,8).Select(mask=>Transform(Enumerable.Range(0,3).Select(i=>paired[2*i+((mask&(1<<i))==0?0:1)]).ToArray(),angle,axis)).ToArray();
   return Enumerable.Range(0,3).SelectMany(i=>new[]{corners.Min(p=>p[i]),corners.Max(p=>p[i])}).ToArray();
  }
  internal static bool ExactBounds(int angle,double[] axis) {
   var columns=Enumerable.Range(0,3).Select(i=>Transform(Enumerable.Range(0,3).Select(j=>i==j?1.0:0.0).ToArray(),angle,axis,true)).ToArray();
   return columns.All(column=>column.Count(v=>Math.Abs(Math.Abs(v)-1)<=1e-10)==1&&column.Count(v=>Math.Abs(v)<=1e-10)==2)&&Enumerable.Range(0,3).All(row=>columns.Count(column=>Math.Abs(Math.Abs(column[row])-1)<=1e-10)==1);
  }
  static bool Near(double[] expected,double[] actual,double absolute,double relative) {
   return expected.Length==actual.Length&&expected.Zip(actual,(e,a)=>Math.Abs(e-a)<=absolute+relative*Math.Max(Math.Abs(e),Math.Abs(a))).All(ok=>ok);
  }
  static void Contained(double[] bounds,double[] envelope,double tolerance) {
   Require(Enumerable.Range(0,3).All(i=>bounds[2*i]>=envelope[2*i]-tolerance&&bounds[2*i+1]<=envelope[2*i+1]+tolerance),"Bounds escape rigid-motion envelope.");
  }
  internal static void Verify(Samples before,Samples after,int angle,double[] axis,double absoluteTolerance,double relativeTolerance) {
   Require(Finite(absoluteTolerance)&&absoluteTolerance>0&&absoluteTolerance<=1&&Finite(relativeTolerance)&&relativeTolerance>=0&&relativeTolerance<=1e-6,"Explicit bounded raw tolerances required.");Valid(before);Valid(after);
   Require(before.Type==after.Type&&before.NativeId==after.NativeId&&before.DescriptorSha256==after.DescriptorSha256,"Measurement identity/domain changed.");
   Require(before.Positions.Select(p=>p.Key).SequenceEqual(after.Positions.Select(p=>p.Key))&&before.Directions.Select(p=>p.Key).SequenceEqual(after.Directions.Select(p=>p.Key)),"Witness anchors/order changed.");
   for(int i=0;i<before.Positions.Length;i++)Require(Near(Transform(before.Positions[i].Value,angle,axis),after.Positions[i].Value,absoluteTolerance,relativeTolerance),"Point does not match explicit rotation.");
   for(int i=0;i<before.Directions.Length;i++)Require(Near(Transform(before.Directions[i].Value,angle,axis,true),after.Directions[i].Value,absoluteTolerance,relativeTolerance),"Direction does not match rotation; pivot must not translate it.");
   Require(Near(before.Invariants,after.Invariants,absoluteTolerance,relativeTolerance),"Rigid-motion invariant changed.");
   var expected=Envelope(before.BoundsPaired,angle,axis);double span=Enumerable.Range(0,3).Max(i=>Math.Max(before.BoundsPaired[2*i+1]-before.BoundsPaired[2*i],after.BoundsPaired[2*i+1]-after.BoundsPaired[2*i]));double boundsTolerance=absoluteTolerance+relativeTolerance*span;
   Require(Finite(span)&&Finite(boundsTolerance),"Bounds tolerance overflow.");
   if(ExactBounds(angle,axis))Require(expected.Zip(after.BoundsPaired,(e,a)=>Math.Abs(e-a)<=boundsTolerance).All(ok=>ok),"Exact signed-permutation bounds differ.");
   else {Contained(after.BoundsPaired,expected,boundsTolerance);Contained(before.BoundsPaired,Envelope(after.BoundsPaired,-angle,axis),boundsTolerance);}
  }
 }
}
