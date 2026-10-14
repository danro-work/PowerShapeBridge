using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PowerShapeGeometry {
 // Pure reference predicate. No CAD, commands, topology proof or fence changes.
 internal static class UniformScaleSampleVerifier {
  internal sealed class Witness {
   internal readonly string Key;
   internal readonly ReadOnlyCollection<double> Value;
   // Positions always use degree 1; direction evidence explicitly distinguishes
   // unit orientation (degree 0) from derivatives/displacements (degree 1).
   internal readonly int Degree;
   internal Witness(string key,double[] value,int degree=1){Key=key;Value=Array.AsReadOnly((double[])value.Clone());Degree=degree;}
  }
  internal sealed class Metric {
   internal readonly string Key;
   internal readonly int Degree;
   internal readonly double Value;
   internal Metric(string key,int degree,double value){Key=key;Degree=degree;Value=value;}
  }
  internal sealed class Samples {
   internal readonly int Type,NativeId;
   internal readonly string Frame,Units,DescriptorSha256;
   internal readonly ReadOnlyCollection<Witness> Positions,Directions;
   internal readonly ReadOnlyCollection<Metric> Metrics;
   internal readonly ReadOnlyCollection<double> BoundsPaired;
   internal Samples(int type,int id,string frame,string units,string descriptor,
    Witness[] positions,Witness[] directions,Metric[] metrics,double[] bounds) {
    Type=type;NativeId=id;Frame=frame;Units=units;DescriptorSha256=descriptor;
    Positions=Array.AsReadOnly((Witness[])positions.Clone());Directions=Array.AsReadOnly((Witness[])directions.Clone());
    Metrics=Array.AsReadOnly((Metric[])metrics.Clone());BoundsPaired=Array.AsReadOnly((double[])bounds.Clone());
   }
  }
  static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
  static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
  static bool Key(string value){return !string.IsNullOrWhiteSpace(value)&&value.Length<=128&&!value.Any(char.IsControl);}
  static void Vector(IList<double> value){Require(value!=null&&value.Count==3&&value.All(Finite),"Finite 3D vector required.");}
  static void Box(IList<double> value){Require(value!=null&&value.Count==6&&value.All(Finite)&&Enumerable.Range(0,3).All(i=>value[2*i]<=value[2*i+1]),"Paired finite bounds required.");}
  internal static void Validate(Samples value) {
   if(value==null)throw new ArgumentNullException("value");
   Require(new[]{40,21,59}.Contains(value.Type)&&value.NativeId>=0&&value.Frame=="world"&&value.Units=="document_raw","Pinned native identity/world/raw units required.");
   Require(value.DescriptorSha256!=null&&value.DescriptorSha256.Length==64&&value.DescriptorSha256.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"Pinned measurement domain required.");
   Require(value.Positions.Count>0&&value.Positions.Count<=65536&&value.Directions.Count<=131072&&value.Metrics.Count<=128,"Bounded nonempty position evidence required.");
   Box(value.BoundsPaired);
   foreach(var list in new[]{value.Positions,value.Directions}) {
    Require(list.All(w=>w!=null&&Key(w.Key))&&list.Select(w=>w.Key).Distinct(StringComparer.Ordinal).Count()==list.Count,"Unique witness anchors required.");
    foreach(var witness in list)Vector(witness.Value);
   }
   foreach(var witness in value.Positions) {
    Require(witness.Degree==1,"Position degree must be 1.");
    Require(Enumerable.Range(0,3).All(i=>witness.Value[i]>=value.BoundsPaired[2*i]&&witness.Value[i]<=value.BoundsPaired[2*i+1]),"Position outside measured bounds.");
   }
   foreach(var witness in value.Directions) {
    Require(witness.Degree==0||witness.Degree==1,"Direction degree must identify unit orientation or derivative.");
    if(witness.Degree==0)Require(Math.Abs(witness.Value.Sum(x=>x*x)-1)<=1e-10,"Unit direction evidence required for degree 0.");
   }
   Require(value.Metrics.All(m=>m!=null&&Key(m.Key)&&m.Degree>=0&&m.Degree<=3&&Finite(m.Value)&&m.Value>=0)&&value.Metrics.Select(m=>m.Key).Distinct(StringComparer.Ordinal).Count()==value.Metrics.Count,"Named nonnegative dimensioned metrics required.");
  }
  static void Parameters(double factor,IList<double> centre) {
   Vector(centre);Require(Finite(factor)&&factor>0&&factor!=1,"Positive finite non-noop uniform factor required; reflections are separate.");
  }
  internal static double[] Transform(double[] point,double factor,double[] centre) {
   Vector(point);Parameters(factor,centre);
   var result=Enumerable.Range(0,3).Select(i=>centre[i]+factor*(point[i]-centre[i])).ToArray();Vector(result);return result;
  }
  internal static double[] Bounds(double[] paired,double factor,double[] centre) {
   Box(paired);Parameters(factor,centre);
   var result=Enumerable.Range(0,6).Select(i=>centre[i/2]+factor*(paired[i]-centre[i/2])).ToArray();Box(result);return result;
  }
  static double Power(double factor,int degree) {
   double result=1;for(int i=0;i<degree;i++)result*=factor;
   Require(Finite(result)&&result>0,"Scale power overflow or underflow.");return result;
  }
  static double Tolerance(double absolute,double relative,double magnitude) {
   double result=absolute+relative*magnitude;Require(Finite(result)&&result>0,"Tolerance overflow.");return result;
  }
  internal static void Verify(Samples before,Samples after,double factor,double[] centre,
   double positionAbsoluteTolerance,double relativeTolerance,IDictionary<string,double> metricAbsoluteTolerances) {
   Parameters(factor,centre);Validate(before);Validate(after);
   Require(Finite(positionAbsoluteTolerance)&&positionAbsoluteTolerance>0&&Finite(relativeTolerance)&&relativeTolerance>=0&&relativeTolerance<=1e-6,"Explicit finite raw tolerances required.");
   Require(before.Type==after.Type&&before.NativeId==after.NativeId&&before.DescriptorSha256==after.DescriptorSha256,"Measurement identity/domain changed; remapping needs a separate proof.");
   foreach(var pair in new[]{new[]{before.Positions,after.Positions},new[]{before.Directions,after.Directions}})
    Require(pair[0].Select(w=>w.Key).SequenceEqual(pair[1].Select(w=>w.Key))&&pair[0].Select(w=>w.Degree).SequenceEqual(pair[1].Select(w=>w.Degree)),"Witness anchors/order/degree changed.");
   Require(before.Metrics.Select(m=>m.Key).SequenceEqual(after.Metrics.Select(m=>m.Key))&&before.Metrics.Select(m=>m.Degree).SequenceEqual(after.Metrics.Select(m=>m.Degree)),"Metric anchors/order/dimensions changed.");
   if(metricAbsoluteTolerances==null)throw new ArgumentNullException("metricAbsoluteTolerances");
   Require(metricAbsoluteTolerances.Count==before.Metrics.Count&&before.Metrics.All(m=>metricAbsoluteTolerances.ContainsKey(m.Key))&&metricAbsoluteTolerances.Values.All(v=>Finite(v)&&v>0),"Explicit absolute tolerance for every metric in its declared raw dimension required.");
   var expectedBounds=Bounds(before.BoundsPaired.ToArray(),factor,centre);
   double spatialMagnitude=Enumerable.Range(0,6).Max(i=>Math.Max(Math.Abs(before.BoundsPaired[i]-centre[i/2]),Math.Abs(expectedBounds[i]-centre[i/2])));
   Require(Finite(spatialMagnitude),"Relative spatial scale overflow.");
   double spatialTolerance=Tolerance(positionAbsoluteTolerance,relativeTolerance,spatialMagnitude);bool detectable=false;
   for(int i=0;i<before.Positions.Count;i++) {
    var expected=Transform(before.Positions[i].Value.ToArray(),factor,centre);
    for(int axis=0;axis<3;axis++) {
     Require(Math.Abs(expected[axis]-after.Positions[i].Value[axis])<=spatialTolerance,"Position does not match uniform scaling about the explicit centre.");
     detectable|=Math.Abs(expected[axis]-before.Positions[i].Value[axis])>spatialTolerance;
    }
   }
   for(int i=0;i<before.Directions.Count;i++) {
    var a=before.Directions[i];var b=after.Directions[i];double multiplier=Power(factor,a.Degree);
    for(int axis=0;axis<3;axis++) {
     double expected=a.Value[axis]*multiplier;Require(Finite(expected),"Direction overflow.");
     double tolerance=a.Degree==0?relativeTolerance:spatialTolerance;
     Require(Math.Abs(expected-b.Value[axis])<=tolerance,"Direction/derivative scale mismatch; centre must not translate it.");
    }
   }
   for(int i=0;i<before.Metrics.Count;i++) {
    var a=before.Metrics[i];var b=after.Metrics[i];double expected=a.Value*Power(factor,a.Degree);
    Require(Finite(expected)&&(a.Value==0||expected>0),"Metric overflow or underflow.");
    double tolerance=Tolerance(metricAbsoluteTolerances[a.Key],relativeTolerance,Math.Max(Math.Abs(expected),Math.Abs(b.Value)));
    Require(Math.Abs(expected-b.Value)<=tolerance,"Dimensioned metric does not match scale power.");
    detectable|=a.Degree>0&&Math.Abs(expected-a.Value)>tolerance;
   }
   for(int i=0;i<6;i++) {
    Require(Math.Abs(expectedBounds[i]-after.BoundsPaired[i])<=spatialTolerance,"Uniform-scale paired bounds differ.");
    detectable|=Math.Abs(expectedBounds[i]-before.BoundsPaired[i])>spatialTolerance;
   }
   Require(detectable,"Evidence cannot distinguish requested scaling from no change at the declared tolerances.");
  }
 }
}
