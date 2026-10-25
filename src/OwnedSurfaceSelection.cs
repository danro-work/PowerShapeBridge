using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace PowerShapeBridge {
 // Measurement provider only. The existing selection executor owns the once-claim,
 // fence, one callback and independent verifier. No bounding-box fallback.
 internal static class OwnedSurfaceSelection {
  internal const string Profile="owned_surface_grid_selection_v1";
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  internal static bool Is(Dictionary<string,object> p){return p!=null&&p.ContainsKey("mode")&&(Equals(p["mode"],"select_surface_single")||Equals(p["mode"],"clear_surface_single"));}
  internal static bool CaptureIs(Dictionary<string,object> p){return p!=null&&p.ContainsKey("profile")&&Equals(p["profile"],Profile);}
  internal static bool Clear(Dictionary<string,object> p){return Equals(p["mode"],"clear")||Equals(p["mode"],"clear_surface_single");}
  internal static void Parameters(Dictionary<string,object> p){Need(Is(p)&&p.Count==3&&p.ContainsKey("entityKey")&&p.ContainsKey("grid"),"Explicit surface selection fields required.");var keys=EntitySets.Keys(new[]{p["entityKey"]});Need(keys.Length==1&&keys[0][0]==21&&keys[0][1]>0,"One explicit SURFACE target required.");var g=p["grid"]as Dictionary<string,object>;NativeSurfaceSampling.Grid(g);Need((int)g["tDivisions"]<=4&&(int)g["uDivisions"]<=4,"Selection witness grid is bounded to four divisions per axis.");}
  internal static string Canonical(object v){
   if(v==null||v is string||v is bool)return Program.Json.Serialize(v);var d=v as IDictionary<string,object>;if(d!=null)return "{"+string.Join(",",d.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>Program.Json.Serialize(k)+":"+Canonical(d[k])))+"}";var a=v as IEnumerable;if(a!=null)return "["+string.Join(",",a.Cast<object>().Select(Canonical))+"]";
   Need(v is int||v is long||v is float||v is double||v is decimal,"Unexpected surface proof value.");double n=Convert.ToDouble(v,CultureInfo.InvariantCulture);Need(!double.IsNaN(n)&&!double.IsInfinity(n),"Nonfinite surface proof.");return n.ToString("R",CultureInfo.InvariantCulture);
  }
  internal static bool Same(object a,object b){return Canonical(a)==Canonical(b);}
  internal static Dictionary<string,object> Capture(Dictionary<string,object> o,DateTime expiry,OwnedSelection.IContext c,Dictionary<string,object> p){
   Parameters(p);var sampler=c as NativeSurfaceSampling.IContext;Need(sampler!=null,"Retained surface sampling context required.");var inventory=EntitySets.Inventory(o).OrderBy(e=>(int)e["nativeId"]).ToArray();Need(inventory.Length>=1&&inventory.Length<=8&&inventory.All(e=>Equals(e["nativeTypeCode"],21))&&Equals(o["lengthUnitsRaw"],"mm")&&Convert.ToDouble(o["lengthUnitFactorRaw"],CultureInfo.InvariantCulture)==1,"Surface selection supports one to eight SURFACEs in mm only.");
   var key=EntitySets.Keys(new[]{p["entityKey"]})[0];var target=inventory.Single(e=>Equals(e["nativeId"],key[1]));string name=(string)target["name"];new OwnedSelectionProgram(true,name,21);Need(inventory.Select(e=>(string)e["name"]).Distinct(StringComparer.Ordinal).Count()==inventory.Length,"Surface names must be unique.");
   var countValues=((IEnumerable)o["contextBefore"]).Cast<object>().ToArray();Need(countValues.Length==2&&countValues[0]is int&&((int)countValues[0]==0||(int)countValues[0]==1)&&Equals(countValues[1],0)&&Same(o["contextBefore"],o["contextAfter"]),"Stable WORLD zero/one selection required.");int count=(int)countValues[0];Need(c.SelectionCount()==count,"Direct selection count differs.");c.ConfirmDocument();string windows=DocumentBindings.WindowDigest(c.Windows());
   var mapping=NativeSelectionReading.Inventory(o,c);int composite=(int)mapping.Single(e=>Equals(e["nativeId"],key[1])&&Equals(e["nativeTypeCode"],21))["compositeId"];Need(composite>0&&c.Composite(21,key[1])==composite,"Surface composite mapping differs.");
   if(count==1){var selected=NativeSelectionReading.Read(o,expiry,c);Need(Equals(selected["state"],"native_selection_items_read_unqualified")&&Equals(selected["selectionCount"],1)&&Same(selected["selectedEntityKeys"],new[]{key}),"Exact positive selected SURFACE proof required.");}
   var grid=(Dictionary<string,object>)p["grid"];var witnesses=new List<object>();foreach(var e in inventory){
    c.ConfirmDocument();var raw=sampler.SurfacePoints((string)e["name"],Convert.ToDouble(grid["tStart"]),Convert.ToDouble(grid["tEnd"]),(int)grid["tDivisions"],Convert.ToDouble(grid["uStart"]),Convert.ToDouble(grid["uEnd"]),(int)grid["uDivisions"]);var decoded=NativeSurfaceSampling.Decode(raw,grid);witnesses.Add(new Dictionary<string,object>{{"entityKey",new[]{21,(int)e["nativeId"]}},{"name",e["name"]},{"samples",decoded}});
   }
   EntitySets.CheckObserved(o,c.Observation(),new int[0][],expiry,DateTime.UtcNow);Need(c.SelectionCount()==count&&windows==DocumentBindings.WindowDigest(c.Windows())&&DateTime.UtcNow<expiry,"Surface sampling changed selection/window/lifetime.");c.ConfirmDocument();
   var result=new Dictionary<string,object>{{"profile",Profile},{"parameters",p},{"count",count},{"entityKey",key},{"bodyName",name},{"compositeId",composite},{"bounds",null},{"boundsStatus","unavailable_sample_witness_only"},{"witnesses",witnesses},{"proofMode",count==0?"explicit_double_count_zero":"native_selected_items_metadata"},{"nullGetterInferredEmpty",false}};
   string json=Program.Json.Serialize(result);Need(Encoding.UTF8.GetByteCount(json)<=32768,"Surface selection witness exceeds bounded packet size.");return Program.Json.Deserialize<Dictionary<string,object>>(json);
  }
 }
}
