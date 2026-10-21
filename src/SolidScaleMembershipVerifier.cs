using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace PowerShapeGeometry {
 // Pure separate predicate for sampled target-surface membership. No CAD, remapping,
 // execution authorization, topology equivalence, journal transitions or fence removal.
 internal static class SolidScaleMembershipVerifier {
  internal const string Profile="solid_uniform_scale_membership_document_tolerance_v1";
  internal const double MaximumDocumentToleranceMm=0.01;
  internal const double BoundsAbsoluteMm=5e-6,CogAbsoluteMm=2e-5,MetricAbsoluteRaw=1e-6,MetricRelative=1e-6;
  internal sealed class Witness {
   internal readonly string Key,FaceObserved;
   internal readonly ReadOnlyCollection<double> Position;
   internal Witness(string key,string faceObserved,double[] position){Key=key;FaceObserved=faceObserved;Position=Array.AsReadOnly((double[])position.Clone());}
  }
  internal sealed class Reading {
   // Context includes retained process/document/windows and unit identities;
   // Metadata includes target native tuple/name/level. Provider must pin their proofs.
   internal readonly string ContextSha256,MetadataSha256,SourceWitnessSha256,Frame,LengthUnits,AreaUnitIdentitySha256,VolumeUnitIdentitySha256;
   internal readonly int NativeType,NativeId;
   internal readonly double LengthFactor,DocumentToleranceMm,AreaRaw,VolumeRaw;
   internal readonly ReadOnlyCollection<double> Bounds,Cog;
   // Ordered surfaces, open links, closed, connected, watertight, trimming valid.
   internal readonly ReadOnlyCollection<int> Invariants;
   internal readonly ReadOnlyCollection<Witness> Witnesses;
   internal Reading(string context,string metadata,string sourceWitness,int type,int id,string frame,string units,double lengthFactor,double tolerance,string areaUnitIdentity,string volumeUnitIdentity,double area,double volume,double[] bounds,double[] cog,int[] invariants,Witness[] witnesses) {
    ContextSha256=context;MetadataSha256=metadata;SourceWitnessSha256=sourceWitness;NativeType=type;NativeId=id;Frame=frame;LengthUnits=units;LengthFactor=lengthFactor;DocumentToleranceMm=tolerance;AreaUnitIdentitySha256=areaUnitIdentity;VolumeUnitIdentitySha256=volumeUnitIdentity;AreaRaw=area;VolumeRaw=volume;
    Bounds=Array.AsReadOnly((double[])bounds.Clone());Cog=Array.AsReadOnly((double[])cog.Clone());Invariants=Array.AsReadOnly((int[])invariants.Clone());Witnesses=Array.AsReadOnly((Witness[])witnesses.Clone());
   }
  }
  internal sealed class Result {
   internal readonly double MembershipBudgetMm,MaximumMembershipCoordinateErrorMm,MaximumMembershipDistanceMm,CogCoordinateErrorMm,BoundsCoordinateErrorMm,AreaErrorRaw,VolumeErrorRaw;
   internal readonly int ChangedObservedFaceNames;
   internal Result(double budget,double point,double distance,double cog,double bounds,double area,double volume,int changes){MembershipBudgetMm=budget;MaximumMembershipCoordinateErrorMm=point;MaximumMembershipDistanceMm=distance;CogCoordinateErrorMm=cog;BoundsCoordinateErrorMm=bounds;AreaErrorRaw=area;VolumeErrorRaw=volume;ChangedObservedFaceNames=changes;}
  }
  static void Need(bool condition,string message){if(!condition)throw new ArgumentException(message);}
  static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
  static bool Hash(string s){return s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');}
  static void Vector(ReadOnlyCollection<double> x){Need(x.Count==3&&x.All(v=>Finite(v)&&Math.Abs(v)<=1000000),"Bounded finite WORLD vector required.");}
  static double Difference(ReadOnlyCollection<double> a,double[] b){return Enumerable.Range(0,b.Length).Max(i=>Math.Abs(a[i]-b[i]));}
  static double[] Transform(ReadOnlyCollection<double> p,double factor,double[] centre){return Enumerable.Range(0,3).Select(i=>centre[i]+factor*(p[i]-centre[i])).ToArray();}
  internal static void Validate(Reading r) {
   if(r==null)throw new ArgumentNullException("r");
   Need(Hash(r.ContextSha256)&&Hash(r.MetadataSha256)&&Hash(r.SourceWitnessSha256),"Pinned context/target/source-witness proofs required.");
   Need(r.NativeType==40&&r.NativeId>0&&r.Frame=="world"&&r.LengthUnits=="mm"&&r.LengthFactor==1,"Only explicit one-SOLID WORLD mm/factor1 readings supported.");
   Need(Finite(r.DocumentToleranceMm)&&r.DocumentToleranceMm>=1e-6&&r.DocumentToleranceMm<=MaximumDocumentToleranceMm,"Explicit bounded pre-existing document tolerance required.");
   Need(Hash(r.AreaUnitIdentitySha256)&&Hash(r.VolumeUnitIdentitySha256),"Pinned raw metric unit observations required; no inferred SI conversion.");
   Need(Finite(r.AreaRaw)&&Finite(r.VolumeRaw)&&r.AreaRaw>0&&r.VolumeRaw>0,"Positive finite raw solid area/volume required.");
   Need(r.Bounds.Count==6&&r.Bounds.All(v=>Finite(v)&&Math.Abs(v)<=1000000)&&Enumerable.Range(0,3).All(i=>r.Bounds[2*i]<r.Bounds[2*i+1]),"Nondegenerate bounded paired WORLD bounds required.");Vector(r.Cog);
   Need(Enumerable.Range(0,3).All(i=>r.Cog[i]>=r.Bounds[2*i]-r.DocumentToleranceMm&&r.Cog[i]<=r.Bounds[2*i+1]+r.DocumentToleranceMm),"Observed COG outside WORLD bounds.");
   Need(r.Invariants.Count==6&&r.Invariants[0]>0&&r.Invariants[0]<=8192&&r.Invariants.Skip(1).SequenceEqual(new[]{0,1,1,1,1}),"Only observed closed/connected/watertight/valid SOLID with zero open links supported.");
   Need(r.Witnesses.Count==8,"Eight explicit source-anchored membership witnesses required.");
   for(int i=0;i<8;i++){
    var w=r.Witnesses[i];if(w==null)throw new ArgumentException("Explicit witness required.");Need(w.Key=="corner-"+i&&!string.IsNullOrWhiteSpace(w.FaceObserved)&&w.FaceObserved.Length<=512&&!w.FaceObserved.Any(char.IsControl),"Ordered source witness keys and observed face labels required.");Vector(w.Position);
    Need(Enumerable.Range(0,3).All(j=>w.Position[j]>=r.Bounds[2*j]-r.DocumentToleranceMm&&w.Position[j]<=r.Bounds[2*j+1]+r.DocumentToleranceMm),"Membership witness outside target WORLD bounds.");
   }
   Need(r.Witnesses.Select(w=>string.Join("/",w.Position.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))).Distinct(StringComparer.Ordinal).Count()>=4,"Four distinct spatial witnesses required.");
  }
  internal static Result Verify(Reading before,Reading after,double factor,double[] centre) {
   Validate(before);Validate(after);Need(Finite(factor)&&factor>=0.01&&factor<=100&&factor!=1,"Bounded positive non-noop uniform factor required.");
   if(centre==null)throw new ArgumentNullException("centre");centre=(double[])centre.Clone();Need(centre.Length==3&&centre.All(v=>Finite(v)&&Math.Abs(v)<=1000000),"Explicit bounded WORLD scale centre required.");
   Need(before.ContextSha256==after.ContextSha256&&before.MetadataSha256==after.MetadataSha256&&before.SourceWitnessSha256==after.SourceWitnessSha256&&before.NativeId==after.NativeId,"Context, target or source-anchor proof changed.");
   Need(before.DocumentToleranceMm==after.DocumentToleranceMm&&before.AreaUnitIdentitySha256==after.AreaUnitIdentitySha256&&before.VolumeUnitIdentitySha256==after.VolumeUnitIdentitySha256,"Document tolerance/raw metric units changed.");
   Need(before.Invariants.SequenceEqual(after.Invariants),"Observed SOLID invariant counts/flags changed.");
   double pointError=0,distanceError=0,detectable=0;int faceChanges=0;
   for(int i=0;i<8;i++){
    var p=Transform(before.Witnesses[i].Position,factor,centre);Need(p.All(Finite),"Transformed witness overflow.");
    pointError=Math.Max(pointError,Difference(after.Witnesses[i].Position,p));detectable=Math.Max(detectable,Difference(before.Witnesses[i].Position,p));
    distanceError=Math.Max(distanceError,Math.Sqrt(Enumerable.Range(0,3).Sum(j=>Math.Pow(after.Witnesses[i].Position[j]-p[j],2))));
    if(before.Witnesses[i].FaceObserved!=after.Witnesses[i].FaceObserved)faceChanges++;
   }
   Need(detectable>2*before.DocumentToleranceMm,"Requested scale is not distinguishable from no change at the document tolerance.");
   Need(distanceError<=before.DocumentToleranceMm,"Transformed source points exceed explicit target-surface membership document tolerance.");
   double[] bounds=Enumerable.Range(0,6).Select(i=>centre[i/2]+factor*(before.Bounds[i]-centre[i/2])).ToArray();double boundsError=Difference(after.Bounds,bounds);Need(bounds.All(Finite)&&boundsError<=BoundsAbsoluteMm,"Uniform scale WORLD bounds differ.");
   var cog=Transform(before.Cog,factor,centre);double cogError=Difference(after.Cog,cog);Need(cog.All(Finite)&&cogError<=CogAbsoluteMm,"Uniform scale COG differs.");
   double area=before.AreaRaw*factor*factor,volume=before.VolumeRaw*factor*factor*factor;Need(Finite(area)&&Finite(volume)&&area>0&&volume>0,"Scaled raw metrics overflow/underflow.");
   double areaError=Math.Abs(after.AreaRaw-area),volumeError=Math.Abs(after.VolumeRaw-volume);
   Need(areaError<=MetricAbsoluteRaw+MetricRelative*Math.Max(Math.Abs(area),Math.Abs(after.AreaRaw)),"Raw area does not follow square scale law.");
   Need(volumeError<=MetricAbsoluteRaw+MetricRelative*Math.Max(Math.Abs(volume),Math.Abs(after.VolumeRaw)),"Raw volume does not follow cube scale law.");
   return new Result(before.DocumentToleranceMm,pointError,distanceError,cogError,boundsError,areaError,volumeError,faceChanges);
  }
 }
}
