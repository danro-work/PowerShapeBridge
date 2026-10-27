using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 // New schema8 only. The schema7 strategy and exact-normal postcondition stay unchanged.
 internal static class OwnedSurfaceSetNumerical {
  internal const string Profile="owned_surface_set_grid_translation_numeric_normals_v1";
  internal const string Qualification="Experimental NEW ordered SURFACE set WORLD/mm translation: target normal components within1e-12 raw, unit normals within1e-9; target sampled positions within1e-6mm. Every other surface, layout, visibility and observed metadata exact. Sequential non-atomic callbacks, per-step authoritative evidence, no replay/save/inverse; sampled proof only, not full topology.";
  internal static Dictionary<string,object> Policy(){return new Dictionary<string,object>{{"schemaVersion",1},{"targetNormalComponentToleranceRaw",1e-12},{"unitNormalLengthToleranceRaw",1e-9},{"targetPositionToleranceMm",1e-6},{"nontargetSamplesExact",true}};}
  internal static void Plan(Dictionary<string,object> plan,bool numerical){if(!Equals(plan["schemaVersion"],numerical?8:7)||!Equals(plan["measurementProfile"],numerical?Profile:OwnedSurfaceSetTranslation.Profile)||(numerical? !plan.ContainsKey("normalPolicy")||!OwnedSurfaceSelection.Same(plan["normalPolicy"],Policy()):plan.ContainsKey("normalPolicy")))throw new InvalidDataException("Explicit surface-set schema/profile/normal policy differs; no cross-profile verification.");}
  internal static void Post(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,object> original,Dictionary<string,object> current,double[] delta,int[][] targets){
   var normalized=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(current));var a=((IEnumerable)original["witnesses"]).Cast<Dictionary<string,object>>().ToArray();var b=((IEnumerable)normalized["witnesses"]).Cast<Dictionary<string,object>>().ToArray();if(a.Length!=b.Length)throw new InvalidDataException("Numeric surface inventory differs.");
   for(int row=0;row<a.Length;row++){
    if(!targets.Any(k=>OwnedSurfaceSelection.Same(k,a[row]["entityKey"])))continue;
    if(!OwnedSurfaceSelection.Same(a[row]["entityKey"],b[row]["entityKey"]))throw new InvalidDataException("Numeric surface witness order differs.");var old=(Dictionary<string,object>)a[row]["samples"];var now=(Dictionary<string,object>)b[row]["samples"];var on=((IEnumerable)old["normalsWorldRaw"]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();var nn=((IEnumerable)now["normalsWorldRaw"]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();if(on.Length!=nn.Length||on.Length<4)throw new InvalidDataException("Numeric target normal shape differs.");
    for(int i=0;i<on.Length;i++){if(Math.Abs(Math.Sqrt(on[i].Sum(v=>v*v))-1)>1e-9||Math.Abs(Math.Sqrt(nn[i].Sum(v=>v*v))-1)>1e-9)throw new InvalidDataException("Numeric target normal is not unit length.");for(int axis=0;axis<3;axis++)if(Math.Abs(on[i][axis]-nn[i][axis])>1e-12)throw new InvalidDataException("Numeric target normal exceeds frozen component tolerance.");}
    now["normalsWorldRaw"]=old["normalsWorldRaw"];
   }
   OwnedSurfaceTranslation.PostTargets(before,after,original,normalized,delta,targets);
  }
 }
}
