using System.Text.Json.Nodes;
namespace PowerShapeMcp;
internal static class SurfaceSetRotationProof
{
 internal const string Profile="owned_surface_set_grid_world_integer_rotation_v1";
 internal static JsonObject Policy()=>new(){["schemaVersion"]=1,["targetPositionToleranceMm"]=2e-6,["targetNormalComponentToleranceRaw"]=2e-6,["unitNormalLengthToleranceRaw"]=1e-9,["nontargetSamplesExact"]=true};
 static void Need(bool v){if(!v)throw new ReferenceRejected("surface_set_rotation_proof_invalid");}
 static string S(JsonObject o,string k)=>ArtifactProof.String(o,k,"surface_set_rotation_proof_invalid");
 internal static void Preparation(string root,JsonObject args){var packet=ArtifactProof.Verified(root,"ownedrotations",S(args,"ownedRotationId"),S(args,"ownedRotationSha256"),65536);Need(ArtifactProof.Boolean(packet["executionEnabled"],false));var p=ArtifactProof.Verified(root,"rotationplans",S(packet,"rotationPlanId"),S(packet,"rotationPlanSha256"),65536);Need(S(p,"operation")=="rotate_entities");_=SurfaceSetTranslationProof.Keys(new(){["targetEntityKeys"]=p["entityKeys"]!.DeepClone()});_=RotationDefinition.Read(p["parameters"]!.AsObject());}
 internal static RotationDefinition Definition(JsonObject p,JsonObject source){
  Need(ArtifactProof.IntegerIs(p["schemaVersion"],9)&&S(p,"measurementProfile")==Profile&&S(p,"operation")=="rotate_owned_surface_set"&&JsonNode.DeepEquals(p["normalPolicy"],Policy())&&!p.ContainsKey("vectorRaw"));
  var def=RotationDefinition.Read(source["parameters"]!.AsObject());int angle=SurfaceReadTools.Integer(p["angleDegrees"]);Need(def.AngleDegrees==angle&&Math.Abs((long)angle)<=1000000&&angle%360!=0);
  var a=p["nativeAxis"]!.AsArray().Select(SurfaceReadTools.Number).ToArray();var b=source["nativeAxis"]!.AsArray().Select(SurfaceReadTools.Number).ToArray();Need(a.Length==6&&b.Length==6&&Math.Abs(a.Skip(3).Sum(x=>x*x)-1)<=1e-12&&def.Origin.SequenceEqual(a.Take(3))&&a.Take(3).SequenceEqual(b.Take(3))&&a.Skip(3).Zip(b.Skip(3),(x,y)=>Math.Abs(x-y)<=1e-12).All(v=>v)&&def.UnitDirection.Zip(a.Skip(3),(x,y)=>Math.Abs(x-y)<=1e-12).All(v=>v));return def;
 }
 internal static void Post(JsonObject original,JsonObject current,JsonArray targets,JsonObject plan){
  var axis=plan["nativeAxis"]!.AsArray().Select(SurfaceReadTools.Number).ToArray();Need(axis.Length==6);var def=new RotationDefinition(SurfaceReadTools.Integer(plan["angleDegrees"]),axis.Take(3).ToArray(),axis.Skip(3).ToArray());
  var normalized=current.DeepClone().AsObject();var old=original["witnesses"]!.AsArray();var now=normalized["witnesses"]!.AsArray();Need(old.Count==now.Count&&targets.Count is >=1 and <=8&&targets.All(k=>old.Count(v=>JsonNode.DeepEquals(v!["entityKey"],k))==1));
  for(int row=0;row<old.Count;row++)if(targets.Any(k=>JsonNode.DeepEquals(k,old[row]!["entityKey"]))){
   Need(JsonNode.DeepEquals(old[row]!["entityKey"],now[row]!["entityKey"]));foreach(var field in new[]{"positionsWorldRaw","normalsWorldRaw"}){var x=old[row]!["samples"]![field]!.AsArray();var y=now[row]!["samples"]![field]!.AsArray();Need(x.Count>=4&&x.Count==y.Count);for(int i=0;i<x.Count;i++){var a=x[i]!.AsArray().Select(SurfaceReadTools.Number).ToArray();var b=y[i]!.AsArray().Select(SurfaceReadTools.Number).ToArray();Need(a.Length==3&&b.Length==3);if(field=="normalsWorldRaw")Need(Math.Abs(Math.Sqrt(a.Sum(v=>v*v))-1)<=1e-9&&Math.Abs(Math.Sqrt(b.Sum(v=>v*v))-1)<=1e-9);var expected=RotationGeometry.RotatePoint(field=="normalsWorldRaw"?def with{Origin=new double[3]}:def,a);Need(expected.Zip(b,(u,v)=>Math.Abs(u-v)<=2e-6).All(v=>v));}now[row]!["samples"]![field]=x.DeepClone();}
  }Need(JsonNode.DeepEquals(original,normalized));
 }
}
