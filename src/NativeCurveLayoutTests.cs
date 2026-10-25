using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 internal static class NativeCurveLayoutTests {
  static void Need(bool v,string s){if(!v)throw new InvalidOperationException(s);}
  static Dictionary<string,object> Clone(object v){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));}
  sealed class Fake:NativeCurveLayout.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal int Calls;internal string Fault;
   public Dictionary<string,object> Observation(){var s=Clone(Snapshot);if(Fault=="context"&&Calls>0)((Dictionary<string,object>)s["model"])["id"]=99;return s;}
   public Dictionary<string,object> Windows(){return Clone(Listing);}public void ConfirmDocument(){}public void Dispose(){}
   public object CurvePoints(string name,int start,int end){Need(name=="Synthetic_59"&&start==0&&end==3,"Explicit getter changed");Calls++;object xyz=new double[,]{{-3.3333333333333348,2,3},{4,5,6},{7,8,9},{10,11,12}};if(Fault=="nonfinite")xyz=new[]{double.NaN};if(Fault=="drift"&&Calls==2)xyz=new[]{123.0};if(Fault=="scalar")xyz="bad";object[] r={Fault=="status"?-1:0,xyz,new double[4,3],new double[4],new bool[4],new bool[4],new double[4,3],new double[4],new bool[4],new bool[4]};if(Fault=="short")return r.Take(9).ToArray();return r;}
  }
  internal static int Run(){Need(File.Exists(Path.Combine(Program.Root,".curve-layout-fixture")),"Marked fake curve fixture required; no CAD.");var rows=new List<object>();foreach(string fault in new[]{"control","nonfinite","drift","scalar","status","short","context","expired","selection","range"}){
   var f=new OwnedOperationPlanTests.Fixture(true,true);var c=new Fake{Snapshot=Clone(f.Snapshot),Listing=Clone(f.Listing),Fault=fault};c.Snapshot["busy"]=false;if(fault=="selection")c.Snapshot["selectionBefore"]=new[]{1};var range=new Dictionary<string,object>{{"startIndex",0},{"endIndex",fault=="range"?32:3}};var r=NativeCurveLayout.Read(Clone(c.Snapshot),new[]{59,10},range,DateTime.UtcNow.AddSeconds(fault=="expired"?-1:120),c);Need(Equals(r["state"],fault=="control"?"curve_layout_read_observed":"curve_layout_read_rejected"),fault+": "+Program.Json.Serialize(r));Need(Equals(r["geometryQualified"],false)&&Equals(r["geometryMutationAttempted"],false),"Read changed qualification/authority");if(fault=="status")Need(Equals(r["getterStatusRaw"],-1),"Native error code lost");if(fault=="control"){Need(c.Calls==2,"Two getter reads required");var fields=(Dictionary<string,object>[])((Dictionary<string,object>)r["nativeLayout"])["fields"];Need((int)fields[0]["rank"]==2&&Equals(((object[])fields[0]["values"])[0],-3.3333333333333348),"Shape/double precision lost");}rows.Add(new{fault,c.Calls,result=r});
  }Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","curve-layout-tests.json"),new{state="passed",rows,cadConnectionAttempted=false});Console.WriteLine("PASS "+rows.Count+" curve layout/context/double/error guards; no CAD.");return rows.Count;}
 }
}
