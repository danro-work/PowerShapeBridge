using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 // Separate explicit-UV affine witness strategy for the existing scale lifecycle.
 internal static class OwnedSurfaceScaleMeasurements {
  internal const string Scope="one_owned_surface_world_uniform_scale_explicit_uv_v1",Profile="surface_uniform_scale_explicit_uv_v1";
  internal static readonly string[] Recipes={"prepare_owned_surface_scale","execute_owned_surface_scale","verify_owned_surface_scale"};
  internal interface IPhaseGuard {void SurfaceScalePhaseGuard(Dictionary<string,object> expected,string name,int phase,int windowCount);}
  static void Need(bool v,string e){if(!v)throw new InvalidDataException(e);}
  static Dictionary<string,object> Clone(Dictionary<string,object> v){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));}
  static Dictionary<string,object> Obj(Dictionary<string,object> v,string k){return(Dictionary<string,object>)v[k];}
  static Dictionary<string,object>[] Rows(Dictionary<string,object> c){return((IEnumerable)c["witnesses"]).Cast<Dictionary<string,object>>().ToArray();}
  static double Number(object v){return OwnedSurfaceTranslation.Number(v);}
  static double[][] Vectors(object v){return((IEnumerable)v).Cast<object>().Select(OwnedSurfaceTranslation.Vector).ToArray();}
  internal static Dictionary<string,object> Basic(Dictionary<string,object> a){Need(a.Count==6&&a.ContainsKey("surfaceGrid"),"Explicit surface scale preparation/grid required.");var grid=Obj(a,"surfaceGrid");NativeSurfaceSampling.Grid(grid);Need((int)grid["tDivisions"]<=4&&(int)grid["uDivisions"]<=4,"Surface scale grid divisions exceed four.");return a.Where(p=>p.Key!="surfaceGrid").ToDictionary(p=>p.Key,p=>p.Value);}
  static Dictionary<string,object> Capture(Dictionary<string,object> o,DateTime expiry,DocumentBindings.IContext c,int native,Dictionary<string,object> grid){var retained=c as OwnedTranslation.IContext;Need(retained!=null,"Retained surface/selection context required.");return OwnedSurfaceTranslation.Capture(o,expiry,retained,native,grid);}
  static Dictionary<string,object> Body(Dictionary<string,object> o,int native){var e=EntitySets.Inventory(o);Need(e.Length>=1&&e.Length<=8&&e.All(r=>Equals(r["nativeTypeCode"],21)),"One to eight SURFACEs required.");return e.Single(r=>Equals(r["nativeId"],native));}
  internal static void Source(Dictionary<string,object> m,Dictionary<string,object> expected,double factor,double[] centre){
   Need(m.Count==7&&Equals(m["schemaVersion"],1)&&Equals(m["profile"],Profile)&&Equals(m["sourceObservationSha256"],EntitySets.ObservedDigest(expected)),"Surface scale source provenance differs.");var key=((IEnumerable)m["entityKey"]).Cast<object>().ToArray();Need(key.Length==2&&Equals(key[0],21),"Explicit SURFACE key required.");int native=Program.StrictInt(key[1]);var b=Body(expected,native);Need(Equals(expected["lengthUnitsRaw"],"mm")&&Number(expected["lengthUnitFactorRaw"])==1&&Number(Obj(b,"geometry")["areaRaw"])>0,"Resolved mm and positive raw area required.");
   Need(factor>=0.01&&factor<=100&&factor!=1&&centre.Length==3&&centre.All(x=>Math.Abs(x)<=1000000),"Bounded scale definition required.");var capture=Obj(m,"captureBefore");Need(OwnedSurfaceSelection.Same(capture["entityKey"],key)&&Equals(capture["compositeId"],m["sourceCompositeId"]),"Source target/composite differs.");var samples=Obj(Rows(capture).Single(r=>OwnedSurfaceSelection.Same(r["entityKey"],key)),"samples");var points=Vectors(samples["positionsWorldRaw"]);Need(points.Length>=4&&points.Length<=25,"Bounded source surface witnesses required.");double motion=0;
   foreach(var p in points)for(int i=0;i<3;i++){double q=centre[i]+factor*(p[i]-centre[i]);Need(Math.Abs(p[i])<=1000000&&!double.IsNaN(q)&&!double.IsInfinity(q)&&Math.Abs(q)<=1000000,"Surface scale coordinate budget exceeded.");motion=Math.Max(motion,Math.Abs(q-p[i]));}Need(motion>0.000004,"Surface scale displacement below observation budget.");
   foreach(var n in Vectors(samples["normalsWorldRaw"]))Need(n.Sum(x=>x*x)>0,"Nonzero surface normal required.");
  }
  internal static Dictionary<string,object> CaptureSource(Dictionary<string,object> o,int native,DateTime expiry,string window,Dictionary<string,object> grid,DocumentBindings.IContext c,double factor,double[] centre){
   var capture=Capture(o,expiry,c,native,grid);var m=new Dictionary<string,object>{{"schemaVersion",1},{"profile",Profile},{"entityKey",new[]{21,native}},{"sourceCompositeId",capture["compositeId"]},{"sourceObservationSha256",EntitySets.ObservedDigest(o)},{"windowContextSha256",window},{"captureBefore",capture}};Source(m,o,factor,centre);return m;
  }
  internal static void RecheckSource(Dictionary<string,object> m,Dictionary<string,object> expected,DateTime expiry,DocumentBindings.IContext c,int native){var old=Obj(m,"captureBefore");var current=Capture(expected,expiry,c,native,Obj(Obj(old,"parameters"),"grid"));Need(OwnedSurfaceSelection.Same(old,current),"Surface source grid changed before scaling.");}
  internal sealed class Result {internal Dictionary<string,object> Observation,Diagnostic;internal double MaximumError;}
  internal static Result MeasureTarget(Dictionary<string,object> m,Dictionary<string,object> before,double factor,double[] centre,DocumentBindings.IContext c,DateTime expiry){
   int native=Program.StrictInt(((IEnumerable)m["entityKey"]).Cast<object>().ToArray()[1]);var old=Obj(m,"captureBefore");var retained=c as OwnedSolidScale.IExecutionContext;Need(retained!=null,"Retained scale context required.");var o=retained.Observation();var fresh=Capture(o,expiry,c,native,Obj(Obj(old,"parameters"),"grid"));var normalized=Clone(fresh);var a=Rows(old);var b=Rows(normalized);Need(a.Length==b.Length,"Surface scale inventory changed.");double error=0,normalError=0;
   for(int row=0;row<a.Length;row++)if(OwnedSurfaceSelection.Same(a[row]["entityKey"],m["entityKey"])){
    var x=Obj(a[row],"samples");var y=Obj(b[row],"samples");var p=Vectors(x["positionsWorldRaw"]);var q=Vectors(y["positionsWorldRaw"]);Need(p.Length==q.Length,"Target UV shape changed.");for(int i=0;i<p.Length;i++)for(int j=0;j<3;j++){double d=Math.Abs(q[i][j]-(centre[j]+factor*(p[i][j]-centre[j])));error=Math.Max(error,d);Need(d<=0.000002,"Scaled target point differs from declared affine mapping.");}
    var n=Vectors(x["normalsWorldRaw"]);var nn=Vectors(y["normalsWorldRaw"]);Need(n.Length==nn.Length,"Normal shape changed.");for(int i=0;i<n.Length;i++){double l=Math.Sqrt(n[i].Sum(v=>v*v)),ll=Math.Sqrt(nn[i].Sum(v=>v*v));Need(l>0&&ll>0,"Zero scaled surface normal.");for(int j=0;j<3;j++){double d=Math.Abs(n[i][j]/l-nn[i][j]/ll);normalError=Math.Max(normalError,d);Need(d<=0.000002,"Scaled surface normal direction changed.");}}y["positionsWorldRaw"]=x["positionsWorldRaw"];y["normalsWorldRaw"]=x["normalsWorldRaw"];
   }
   Need(OwnedSurfaceSelection.Same(old,normalized),"Other surfaces/visibility/layout/identity changed during scale.");var adjusted=Clone(o);var oldArea=Number(Obj(Body(before,native),"geometry")["areaRaw"]);var g=Obj(Body(adjusted,native),"geometry");double wanted=oldArea*factor*factor,areaError=Math.Abs(Number(g["areaRaw"])-wanted);Need(areaError<=Math.Max(1e-9,Math.Abs(wanted)*1e-9),"Surface raw area does not follow square law.");g["areaRaw"]=oldArea;Need(EntitySets.ObservedDigest(adjusted)==EntitySets.ObservedDigest(before),"Surface scale changed other observed properties/context.");
   return new Result{Observation=o,MaximumError=error,Diagnostic=new Dictionary<string,object>{{"profile",Profile},{"captureAfter",fresh},{"captureBefore",old},{"sourceObservationSha256",m["sourceObservationSha256"]},{"maximumPointComponentErrorMm",error},{"maximumNormalDirectionError",normalError},{"areaErrorRaw",areaError},{"sampleBudgetMm",0.000002},{"nativeBoundsAvailable",false},{"fullTopologyEquivalent",false}}};
  }
  internal static Dictionary<string,object> Finish(Dictionary<string,object> r,bool surface){if(surface){r["qualificationScope"]=Scope;r["state"]=((string)r["state"]).Replace("owned_solid_scale_","owned_surface_scale_");}return r;}
 }
}
