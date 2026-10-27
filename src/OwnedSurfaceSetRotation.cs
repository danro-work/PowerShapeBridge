using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;
namespace PowerShapeBridge {
 // Rotation strategy for the shared ordered-step executor; not a second lifecycle.
 internal static class OwnedSurfaceSetRotation {
  internal const string Profile="owned_surface_set_grid_world_integer_rotation_v1";
  internal const string Operation="rotate_owned_surface_set";
  internal const string Qualification="Experimental ordered2..8owned SURFACE integer-degree WORLD rotation about an explicit pivot/unit axis in raw mm. Target sampled positions within2e-6mm, rotated normal components within2e-6raw, unit lengths within1e-9; non-target witnesses/layout/flags exact. Sequential non-atomic calls with authoritative per-step records, stop on unknown, no inverse/replay/save; sampled geometry only, not full topology.";
  internal static Dictionary<string,object> Policy(){return new Dictionary<string,object>{{"schemaVersion",1},{"targetPositionToleranceMm",2e-6},{"targetNormalComponentToleranceRaw",2e-6},{"unitNormalLengthToleranceRaw",1e-9},{"nontargetSamplesExact",true}};}
  static void Need(bool v,string why){if(!v)throw new InvalidDataException(why);}
  internal static double[] Axis(Dictionary<string,object> plan){
   Need(Equals(plan["schemaVersion"],9)&&Equals(plan["operation"],Operation)&&Equals(plan["measurementProfile"],Profile)&&plan.ContainsKey("normalPolicy")&&OwnedSurfaceSelection.Same(plan["normalPolicy"],Policy())&&!plan.ContainsKey("vectorRaw"),"Frozen set-rotation schema/profile/policy differs.");
   int angle=OwnedSurfaceRotation.Angle(plan["angleDegrees"]);var axis=((IEnumerable)plan["nativeAxis"]).Cast<object>().Select(OwnedSurfaceTranslation.Number).ToArray();
   return OwnedSurfaceRotation.Axis(new Dictionary<string,object>{{"angleDegrees",angle},{"nativeAxis",axis},{"measurementProfile",OwnedSurfaceRotation.SelectProfile(angle,axis)}});
  }
  internal static void Post(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,object> original,Dictionary<string,object> current,double[] axis,int angle,int[][] targets){
   Need(EntitySets.ObservedDigest(before)==EntitySets.ObservedDigest(after),"Set rotation changed observed metadata/context.");
   var normalized=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(current));var old=((IEnumerable)original["witnesses"]).Cast<Dictionary<string,object>>().ToArray();var now=((IEnumerable)normalized["witnesses"]).Cast<Dictionary<string,object>>().ToArray();
   Need(old.Length==now.Length&&targets.Length>=1&&targets.Length<=8&&targets.All(k=>old.Count(r=>OwnedSurfaceSelection.Same(r["entityKey"],k))==1),"Unique rotation targets in complete capture required.");
   for(int row=0;row<old.Length;row++)if(targets.Any(k=>OwnedSurfaceSelection.Same(k,old[row]["entityKey"]))){
    Need(OwnedSurfaceSelection.Same(old[row]["entityKey"],now[row]["entityKey"]),"Rotation target witness order differs.");var a=(Dictionary<string,object>)old[row]["samples"];var b=(Dictionary<string,object>)now[row]["samples"];
    foreach(string field in new[]{"positionsWorldRaw","normalsWorldRaw"}){
     var x=((IEnumerable)a[field]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();var y=((IEnumerable)b[field]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();Need(x.Length>=4&&x.Length==y.Length,"Set rotation sample shape differs.");
     for(int i=0;i<x.Length;i++){if(field=="normalsWorldRaw")Need(Math.Abs(Math.Sqrt(x[i].Sum(v=>v*v))-1)<=1e-9&&Math.Abs(Math.Sqrt(y[i].Sum(v=>v*v))-1)<=1e-9,"Set rotation normals must be unit length.");var expected=angle==0?x[i]:RotationSampleVerifier.Transform(x[i],angle,axis,field=="normalsWorldRaw");Need(expected.Zip(y[i],(u,v)=>Math.Abs(u-v)<=2e-6).All(v=>v),"Set "+field+" differs from declared rotation.");}b[field]=a[field];
    }
   }
   Need(OwnedSurfaceSelection.Same(original,normalized),"Rotation changed non-target samples, layout, flags or identity.");
  }
 }
}
