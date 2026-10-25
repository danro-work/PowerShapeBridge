using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // A passive grid result, deliberately separate from the box-requiring transform
 // witness adapter. No selection, activation, Exec, saving or bbox fallback.
 internal static class NativeSurfaceSampling {
  internal const string Recipe="inspect_surface_samples";
  internal const string Profile="surface_uv_endpoint_samples_v1";
  internal interface IContext:DocumentBindings.IContext {
   object SurfacePoints(string name,double tStart,double tEnd,int tDivisions,double uStart,double uEnd,int uDivisions);
  }
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  static void Fields(Dictionary<string,object> value,params string[] fields){Need(value!=null&&value.Count==fields.Length&&fields.All(value.ContainsKey),"Surface sampling fields differ.");}
  static double Number(object value){Need(value is int||value is long||value is float||value is double||value is decimal,"Finite native number required.");double n=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);Need(!double.IsNaN(n)&&!double.IsInfinity(n),"Nonfinite surface sample.");return n;}
  static object[] Values(object value,int count,params int[] dimensions){var array=value as Array;var list=value as IList;Need(array!=null?array.Rank==1||(array.Rank==dimensions.Length&&Enumerable.Range(0,array.Rank).All(i=>array.GetLength(i)==dimensions[i])):list!=null&&!(value is string),"Surface COM array dimensions differ from the explicit endpoint grid.");var data=((IEnumerable)value).Cast<object>().ToArray();Need(data.Length==count,"Surface response array size differs from endpoint-inclusive divisions.");return data;}
  static object Layout(object value){var a=value as Array;return a==null?(object)new{kind="list"}:new{kind="array",rank=a.Rank,lengths=Enumerable.Range(0,a.Rank).Select(a.GetLength).ToArray(),lowerBounds=Enumerable.Range(0,a.Rank).Select(a.GetLowerBound).ToArray()};}
  internal static void Grid(Dictionary<string,object> grid){
   Fields(grid,"tStart","tEnd","tDivisions","uStart","uEnd","uDivisions");
   foreach(string k in new[]{"tStart","tEnd","uStart","uEnd"})Need(Math.Abs(Number(grid[k]))<=1000000,"Parameter outside bounded range.");
   Need(Number(grid["tStart"])<Number(grid["tEnd"])&&Number(grid["uStart"])<Number(grid["uEnd"]),"Explicit increasing parameter ranges required.");
   foreach(string k in new[]{"tDivisions","uDivisions"})Need(grid[k]is int&&(int)grid[k]>=1&&(int)grid[k]<=32,"Integer divisions1..32 required.");
  }
  internal static void Validate(Dictionary<string,object> args){
   Fields(args,"inspectionRequestId","inspectionSha256","expiresUtc","entityKey","grid");Need(EntitySets.Hex(args["inspectionRequestId"],32)&&EntitySets.Hex(args["inspectionSha256"],64),"Pinned source inspection required.");EntitySets.Expiry(args["expiresUtc"]);var keys=EntitySets.Keys(new[]{args["entityKey"]});Need(keys.Length==1&&keys[0][0]==21&&keys[0][1]>0,"One explicit SURFACE required.");Grid(args["grid"]as Dictionary<string,object>);
  }
  internal static Dictionary<string,object> Decode(object raw,Dictionary<string,object> grid){
   Grid(grid);int tp=(int)grid["tDivisions"]+1,up=(int)grid["uDivisions"]+1,count=tp*up;var response=Values(raw,4);Need(response[0]is int&&Equals(response[0],0),"GetSurfacePoints must report native success0.");
   var positions=Values(response[1],3*count,tp,up,3).Select(Number).ToArray();var normals=Values(response[2],3*count,tp,up,3).Select(Number).ToArray();var visible=Values(response[3],count,tp,up);Need(visible.All(v=>v is bool),"Native visibility values must be BOOL.");
   return new Dictionary<string,object>{{"shape",new Dictionary<string,object>{{"tPoints",tp},{"uPoints",up},{"pointCount",count},{"order","native_reported_order"}}},{"nativeArrayLayout",new{response=Layout(raw),positions=Layout(response[1]),normals=Layout(response[2]),visibility=Layout(response[3])}},{"positionsWorldRaw",Enumerable.Range(0,count).Select(i=>positions.Skip(3*i).Take(3).ToArray()).ToArray()},{"normalsWorldRaw",Enumerable.Range(0,count).Select(i=>normals.Skip(3*i).Take(3).ToArray()).ToArray()},{"visibility",visible.Cast<bool>().ToArray()}};
  }
  static void EmptyWorld(Dictionary<string,object> o){Need(EntitySets.Canonical(o["contextBefore"])=="[0,0]"&&EntitySets.Canonical(o["contextAfter"])=="[0,0]"&&EntitySets.Canonical(o["selectionBefore"])=="[]"&&EntitySets.Canonical(o["selectionAfter"])=="[]"&&Equals(o["busy"],false),"WORLD/empty/idle required; this read never clears selection or changes workplane.");}
  internal static Dictionary<string,object> Read(Dictionary<string,object> expected,int[] key,Dictionary<string,object> grid,DateTime expiry,IContext c){
   var result=new Dictionary<string,object>{{"state","surface_samples_read_rejected"},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"captureAttempted",false},{"nativeLayoutQualified",false},{"retryAllowed",false},{"profile",Profile}};
   try{
    Grid(grid);Need(key!=null&&key.Length==2&&key[0]==21&&key[1]>0,"One SURFACE tuple required.");EmptyWorld(expected);EntitySets.CheckObserved(expected,expected,new[]{key},expiry,DateTime.UtcNow);var entity=EntitySets.Inventory(expected).Single(e=>Equals(e["nativeTypeCode"],21)&&Equals(e["nativeId"],key[1]));string name=entity["name"]as string;Need(!string.IsNullOrWhiteSpace(name)&&name.Length<=512&&EntitySets.Inventory(expected).Count(e=>Equals(e["nativeTypeCode"],21)&&Equals(e["name"],name))==1,"Surface name absent or ambiguous.");Need(expected["lengthUnitsRaw"]is string&&Number(expected["lengthUnitFactorRaw"])>0,"Observed document units/factor required.");
    c.ConfirmDocument();var windows=c.Windows();string digest=DocumentBindings.WindowDigest(windows);var before=c.Observation();EmptyWorld(before);EntitySets.CheckObserved(expected,before,new[]{key},expiry,DateTime.UtcNow);
    var first=c.SurfacePoints(name,Number(grid["tStart"]),Number(grid["tEnd"]),(int)grid["tDivisions"],Number(grid["uStart"]),Number(grid["uEnd"]),(int)grid["uDivisions"]);result["rawResponse"]=first;var decoded=Decode(first,grid);
    var second=c.SurfacePoints(name,Number(grid["tStart"]),Number(grid["tEnd"]),(int)grid["tDivisions"],Number(grid["uStart"]),Number(grid["uEnd"]),(int)grid["uDivisions"]);var decodedSecond=Decode(second,grid);Need(Program.Json.Serialize(first)==Program.Json.Serialize(second)&&Program.Json.Serialize(decoded)==Program.Json.Serialize(decodedSecond),"Surface samples changed between passive getters.");
    var after=c.Observation();EmptyWorld(after);EntitySets.CheckObserved(expected,after,new[]{key},expiry,DateTime.UtcNow);var finalWindows=c.Windows();c.ConfirmDocument();Need(DocumentBindings.WindowDigest(finalWindows)==digest&&DateTime.UtcNow<expiry,"Surface read window/context/lifetime changed.");
    result["state"]="surface_samples_read_observed";result["entityKey"]=key;result["entity"]=entity;result["grid"]=grid;foreach(var pair in decoded)result[pair.Key]=pair.Value;result["rawResponse"]=first;result["before"]=before;result["after"]=after;result["windowsBefore"]=windows;result["windowsAfter"]=finalWindows;result["windowContextSha256"]=digest;result["coordinateFrame"]="world";result["lengthUnits"]="document_raw";result["lengthUnitsRaw"]=expected["lengthUnitsRaw"];result["lengthUnitFactorRaw"]=expected["lengthUnitFactorRaw"];result["parameterRangeInferred"]=false;result["boundsWorldRawPaired"]=null;result["boundsStatus"]="unavailable_in_sample_only_profile";result["topologyCertified"]=false;result["qualification"]="Explicit endpoint-inclusive parameter samples in native reported order. Normals/visibility are returned without reinterpretation. Parameter domain, trimming coverage and full geometry are not certified; no bounds are inferred from samples. No CAD file bytes are verified by this read.";
   }catch(Exception e){result["error"]=e.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run(Dictionary<string,object> args){
   Validate(args);var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.surfaceSamplingExperimental,"Surface sampling disabled.");using(var proofs=new OwnedOperationPlans.Proofs()){
    var expected=proofs.Json(Path.Combine(Program.Root,"responses",(string)args["inspectionRequestId"]+".json"),(string)args["inspectionSha256"],16000000);var key=EntitySets.Keys(new[]{args["entityKey"]})[0];using(var context=new DocumentBindings.ComContext(config)){
     var value=Read(expected,key,(Dictionary<string,object>)args["grid"],EntitySets.Expiry(args["expiresUtc"]),context);value["arguments"]=args;value["nativeProviderQualified"]=true;value["bridgeVersion"]=Program.ReleaseVersion;return value;
    }
   }
  }
 }
}
