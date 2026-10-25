using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 internal static class NativeSurfaceSamplingTests {
  static Dictionary<string,object> Copy(object o){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(o));}
  static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  sealed class Context:NativeSurfaceSampling.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int Calls;
   public Dictionary<string,object> Observation(){var o=Copy(Snapshot);if(Fault=="context"&&Calls==2)((Dictionary<string,object>)o["model"])["id"]=99;return o;}
   public Dictionary<string,object> Windows(){return Copy(Listing);}
   public void ConfirmDocument(){}
   public void Dispose(){}
   static Array ComArray(object[] values,int[] lengths){var a=Array.CreateInstance(typeof(object),lengths,Enumerable.Repeat(1,lengths.Length).ToArray());int i=0;foreach(var v in values){int n=i++;var indexes=new int[lengths.Length];for(int k=lengths.Length-1;k>=0;k--){indexes[k]=n%lengths[k]+1;n/=lengths[k];}a.SetValue(v,indexes);}return a;}
   public object SurfacePoints(string name,double ts,double te,int td,double us,double ue,int ud){Calls++;Need(name=="Synthetic_21"&&td==2&&ud==3,"Unexpected source/grid");int count=Fault=="old_shape"?td*ud:(td+1)*(ud+1);var points=Enumerable.Range(0,3*count).Select(i=>(double)i).ToArray();var normals=new double[3*count];object visible=Enumerable.Repeat(true,count).ToArray();if(Fault=="precise"||Fault=="ulp_drift")points[0]=-3.3333333333333348;if(Fault=="ulp_drift"&&Calls==2)points[0]=-3.3333333333333344;if(Fault=="nonfinite")points[0]=double.NaN;if(Fault=="numeric_visibility")visible=new int[count];if(Fault=="hidden"){var flags=(bool[])visible;flags[0]=false;}if(Fault=="drift"&&Calls==2)points[0]=12345;if(Fault=="com_shape"||Fault=="swapped_shape"){var dims=Fault=="com_shape"?new[]{td+1,ud+1,3}:new[]{ud+1,td+1,3};return ComArray(new object[]{0,ComArray(points.Cast<object>().ToArray(),dims),ComArray(normals.Cast<object>().ToArray(),dims),ComArray(((bool[])visible).Cast<object>().ToArray(),new[]{td+1,ud+1})},new[]{4});}return new object[]{Fault=="native_error"?-3:0,points,normals,visible};}
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".surface-sampling-fixture")),"Isolated surface-sampling marker required; never CAD.");var rows=new List<object>();
   foreach(string fault in new[]{"control","hidden","precise","ulp_drift","com_shape","swapped_shape","old_shape","native_error","nonfinite","numeric_visibility","drift","context","selection","expired"}){
    var f=new OwnedOperationPlanTests.Fixture(true,true);var c=new Context{Snapshot=Copy(f.Snapshot),Listing=Copy(f.Listing),Fault=fault};c.Snapshot["busy"]=false;if(fault=="selection")c.Snapshot["contextBefore"]=c.Snapshot["contextAfter"]=new[]{1,0};
    var grid=new Dictionary<string,object>{{"tStart",1},{"tEnd",2},{"tDivisions",2},{"uStart",1},{"uEnd",2},{"uDivisions",3}};var r=NativeSurfaceSampling.Read(Copy(c.Snapshot),new[]{21,8},grid,DateTime.UtcNow.AddSeconds(fault=="expired"?-1:180),c);bool good=fault=="control"||fault=="hidden"||fault=="precise"||fault=="com_shape";Need(Equals(r["state"],good?"surface_samples_read_observed":"surface_samples_read_rejected"),fault+": "+Program.Json.Serialize(r));foreach(string flag in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","captureAttempted"})Need(Equals(r[flag],false),"Read gained write/capture authority.");if(good){Need(c.Calls==2&&Equals(((Dictionary<string,object>)r["shape"])["pointCount"],12)&&r["boundsWorldRawPaired"]==null&&Equals(r["parameterRangeInferred"],false),"Wrong endpoint/profile/bounds contract");}if(fault=="precise")Need(((double[][])r["positionsWorldRaw"])[0][0]==-3.3333333333333348,"Native double was rounded through string formatting.");rows.Add(new{fault,calls=c.Calls,result=r});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","surface-sampling-tests.json"),new{state="passed",rows,cadConnectionAttempted=false});Console.WriteLine("PASS: "+rows.Count+" surface endpoint-grid/shape/context/error guards; no CAD.");return rows.Count;
  }
 }
}
