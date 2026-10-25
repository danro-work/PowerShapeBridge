using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;
namespace PowerShapeBridge {
 internal static class OwnedSurfaceRotation {
  internal interface IWriter {int Rotate(int composite,int integerDegrees,double[] pointAndUnitDirection);}
  internal const string Profile="owned_surface_grid_world_z90_v1", IntegerProfile="owned_surface_grid_world_integer_v2";
  internal const string Qualification="Experimental one owned SURFACE integer-degree rotation about an explicit WORLD point/unit direction in mm among1..8SURFACEs. Explicit UV point/normal samples; native bounds unavailable, not full topology. No selection/save/inverse/replay; independent verification required.";
  static void Need(bool v,string why){if(!v)throw new InvalidDataException(why);}
  internal static Dictionary<string,object> Validate(Dictionary<string,object> args){Need(args!=null&&args.Count==3&&args.ContainsKey("surfaceGrid"),"Explicit surface rotation fields required.");var basic=args.Where(p=>p.Key!="surfaceGrid").ToDictionary(p=>p.Key,p=>p.Value);OwnedRotationExecution.Validate(basic);var grid=args["surfaceGrid"]as Dictionary<string,object>;NativeSurfaceSampling.Grid(grid);Need((int)grid["tDivisions"]<=4&&(int)grid["uDivisions"]<=4,"Surface rotation grid bound exceeded.");return basic;}
  internal static int Angle(object value){double n=OwnedSurfaceTranslation.Number(value);Need(n==Math.Truncate(n)&&Math.Abs(n)<=1000000&&n%360!=0,"Bounded non-noop integer angle required.");return checked((int)n);}
  internal static string SelectProfile(int angle,double[] axis){return angle==90&&axis.SequenceEqual(new[]{0.0,0,0,0,0,1})?Profile:IntegerProfile;}
  internal static double[] Axis(Dictionary<string,object> plan){var values=plan["nativeAxis"]as IList;Need(values!=null&&values.Count==6,"Native rotation axis shape differs.");var axis=values.Cast<object>().Select(OwnedSurfaceTranslation.Number).ToArray();int angle=Angle(plan["angleDegrees"]);Need(axis.Take(3).All(v=>Math.Abs(v)<=1000000)&&Math.Abs(axis.Skip(3).Sum(v=>v*v)-1)<=1e-12,"Bounded pivot/unit direction required.");Need(Equals(plan["measurementProfile"],SelectProfile(angle,axis)),"Rotation profile/definition differs.");return axis;}
  internal static int Target(OwnedTransformSource d){Need(Equals(d.Plan["operation"],"rotate_entities")&&d.Keys.Length==1&&d.Keys[0][0]==21,"One explicit SURFACE rotation required.");Need(Equals(d.Expected["lengthUnitsRaw"],"mm")&&OwnedSurfaceTranslation.Number(d.Expected["lengthUnitFactorRaw"])==1,"Surface rotation requires raw mm.");var p=(Dictionary<string,object>)d.Plan["parameters"];int angle=Angle(p["angleDegrees"]);var axis=OwnedRotationPlans.Axis(p);Axis(new Dictionary<string,object>{{"angleDegrees",angle},{"nativeAxis",axis},{"measurementProfile",SelectProfile(angle,axis)}});return d.Keys[0][1];}
  internal static int Call(OwnedTranslation.IContext c,int composite,int angle,double[] axis){var writer=c as IWriter;Need(writer!=null,"Retained native rotation writer required.");return writer.Rotate(composite,angle,axis);}
  internal static void Post(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,object> original,Dictionary<string,object> current,double[] axis,int angle=90){
   Need(EntitySets.ObservedDigest(before)==EntitySets.ObservedDigest(after),"Surface rotation changed observed non-positional properties/context.");var normalized=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(current));var key=original["entityKey"];var a=((IEnumerable)original["witnesses"]).Cast<Dictionary<string,object>>().ToArray();var b=((IEnumerable)normalized["witnesses"]).Cast<Dictionary<string,object>>().ToArray();Need(a.Length==b.Length&&a.Count(r=>OwnedSurfaceSelection.Same(r["entityKey"],key))==1,"Unique surface rotation target required.");
   for(int row=0;row<a.Length;row++)if(OwnedSurfaceSelection.Same(a[row]["entityKey"],key)){
    Need(OwnedSurfaceSelection.Same(a[row]["entityKey"],b[row]["entityKey"]),"Surface rotation target order differs.");var old=(Dictionary<string,object>)a[row]["samples"];var fresh=(Dictionary<string,object>)b[row]["samples"];
    foreach(string field in new[]{"positionsWorldRaw","normalsWorldRaw"}){var x=((IEnumerable)old[field]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();var y=((IEnumerable)fresh[field]).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();Need(x.Length>=4&&x.Length==y.Length,"Rotation sample shape differs.");for(int i=0;i<x.Length;i++){var expected=RotationSampleVerifier.Transform(x[i],angle,axis,field=="normalsWorldRaw");Need(expected.Zip(y[i],(u,v)=>Math.Abs(u-v)<=0.000002).All(v=>v),"Surface "+field+" do not match declared rotation.");}fresh[field]=old[field];}
   }
   Need(OwnedSurfaceSelection.Same(original,normalized),"Surface rotation changed other-surface samples/visibility/layout/identity.");
  }
  internal static Dictionary<string,object> Finish(Dictionary<string,object> result,bool rotate){if(rotate){result["state"]=((string)result["state"]).Replace("owned_translation_","owned_surface_rotation_");result["qualification"]=Qualification;}return result;}
 }
}
