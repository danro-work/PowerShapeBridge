using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 // Additive witness provider. OwnedTranslation retains ownership, once-claim,
 // authoritative return, matching fence and independent verification lifecycle.
 internal static class OwnedSurfaceTranslation {
  internal const string Profile="owned_surface_grid_translation_v1";
  internal const string Qualification="Experimental one explicit owned SURFACE WORLD translation in mm among1..8SURFACEs. Explicit UV samples only: target positions shifted, normals/visibility/layout and other surfaces preserved. Native bounds unavailable; not full topology. No selection/save/inverse/replay.";
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  internal static double Number(object v){Need(v is int||v is long||v is double||v is float||v is decimal,"Numeric surface translation measurement required.");double n=Convert.ToDouble(v,CultureInfo.InvariantCulture);Need(!double.IsNaN(n)&&!double.IsInfinity(n),"Nonfinite surface translation measurement.");return n;}
  internal static double[] Vector(object v){var a=v as IList;Need(a!=null&&a.Count==3,"Surface translation vector shape differs.");return a.Cast<object>().Select(Number).ToArray();}
  internal static Dictionary<string,object> Validate(Dictionary<string,object> args){Need(args!=null&&args.Count==3&&args.ContainsKey("surfaceGrid"),"Explicit surface translation fields required.");var basic=args.Where(p=>p.Key!="surfaceGrid").ToDictionary(p=>p.Key,p=>p.Value);OwnedOperationPlans.ValidateParameters(basic,false);var g=args["surfaceGrid"]as Dictionary<string,object>;NativeSurfaceSampling.Grid(g);Need((int)g["tDivisions"]<=4&&(int)g["uDivisions"]<=4,"Surface translation grid bounded to four divisions per axis.");return basic;}
  internal static int Target(OwnedOperationPlans.Dependencies d){Need(Equals(d.Plan["operation"],"translate_entities")&&d.Keys.Length==1&&d.Keys[0][0]==21,"One explicit SURFACE translation required.");Need(Equals(d.Expected["lengthUnitsRaw"],"mm")&&Number(d.Expected["lengthUnitFactorRaw"])==1,"Surface translation requires raw mm.");return d.Keys[0][1];}
  internal static Dictionary<string,object> Capture(Dictionary<string,object> o,DateTime expiry,OwnedTranslation.IContext c,int native,Dictionary<string,object> grid){
   Need(EntitySets.Canonical(o["contextBefore"])=="[0,0]"&&EntitySets.Canonical(o["contextAfter"])=="[0,0]"&&EntitySets.Canonical(o["selectionBefore"])=="[]"&&EntitySets.Canonical(o["selectionAfter"])=="[]"&&Equals(o["busy"],false),"Empty selection/idle/WORLD required for surface translation.");var selection=c as OwnedSelection.IContext;Need(selection!=null,"Retained native inventory context required.");
   return OwnedSurfaceSelection.Capture(o,expiry,selection,new Dictionary<string,object>{{"mode","select_surface_single"},{"entityKey",new[]{21,native}},{"grid",grid}});
  }
  internal static void Post(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,object> original,Dictionary<string,object> current,double[] delta){
   // SURFACE observation has non-positional properties (including raw area).
   // Reject any change rather than normalizing away unsupported new properties.
   Need(EntitySets.ObservedDigest(before)==EntitySets.ObservedDigest(after),"Surface translation changed observed non-positional properties/context.");
   var normalized=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(current));var key=original["entityKey"];
   var oldRows=((IEnumerable)original["witnesses"]).Cast<Dictionary<string,object>>().ToArray();var newRows=((IEnumerable)normalized["witnesses"]).Cast<Dictionary<string,object>>().ToArray();Need(oldRows.Length==newRows.Length&&oldRows.Count(row=>OwnedSurfaceSelection.Same(row["entityKey"],key))==1,"Surface inventory/unique target witness count differs.");
   for(int row=0;row<oldRows.Length;row++){
    if(!OwnedSurfaceSelection.Same(oldRows[row]["entityKey"],key))continue;
    Need(OwnedSurfaceSelection.Same(oldRows[row]["entityKey"],newRows[row]["entityKey"]),"Surface target order differs.");var a=(Dictionary<string,object>)oldRows[row]["samples"];var b=(Dictionary<string,object>)newRows[row]["samples"];var oldPoints=((IEnumerable)a["positionsWorldRaw"]).Cast<object>().Select(Vector).ToArray();var newPoints=((IEnumerable)b["positionsWorldRaw"]).Cast<object>().Select(Vector).ToArray();Need(oldPoints.Length==newPoints.Length&&oldPoints.Length>=4,"Surface point witness shape differs.");
    for(int i=0;i<oldPoints.Length;i++)for(int axis=0;axis<3;axis++)Need(Math.Abs(newPoints[i][axis]-(oldPoints[i][axis]+delta[axis]))<=0.000001,"Surface position does not match declared WORLD translation.");b["positionsWorldRaw"]=a["positionsWorldRaw"];
   }
   Need(OwnedSurfaceSelection.Same(original,normalized),"Surface translation changed normals/visibility/layout/other-surface samples or identity.");
  }
 }
}
