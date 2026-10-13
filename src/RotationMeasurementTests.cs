using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class RotationMeasurementTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Fake : RotationMeasurements.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int Reads,Calls;
   public Dictionary<string,object> Observation(){Reads++;return Clone(Snapshot);}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){}
   public int Composite(int type,int id){return Fault=="mapping_changed"&&Calls>0?999:type;}
   public double[] Bounds(int composite){return new double[]{0,1,0,1,0,1};}
   object Alter(object[] raw){Calls++;if(Fault=="error_code")raw[0]=1;if(Fault=="changed_process")Snapshot["processId"]=99;if(Fault=="nonfinite")((double[])raw[1])[0]=double.NaN;if(Fault=="bool_numeric")raw[1]=new object[]{true,0,0};if(Fault=="array_rank")raw[1]=new double[3,3];if(Fault=="short_points")raw[1]=new double[2];return raw;}
   public object SurfacePoints(string name,double ts,double te,int tc,double us,double ue,int uc) {
    Require(name=="Synthetic_21"&&ts==0&&te==1&&us==0&&ue==1&&tc==2&&uc==2,"Explicit surface descriptor changed.");
    object[] raw={0,new double[]{0,0,0,1,0,0,0,1,0,1,1,0},new double[]{0,0,1,0,0,1,0,0,1,0,0,1},new[]{true,true,true,true}};
    if(Fault=="flag_numeric")raw[3]=new[]{1,1,1,1};if(Fault=="short_normals")raw[2]=new double[3];return Alter(raw);
   }
   public object CurvePoints(string name,int start,int end) {
    Require(name=="Synthetic_59"&&start==0&&end==1,"Explicit curve descriptor changed.");
    object[] raw={0,new double[]{0,0,0,1,0,0},new double[]{1,0,0,1,0,0},new double[]{1,1,1,1,1,1},new[]{true,true},new[]{true,true},new double[]{1,0,0,1,0,0},new double[]{1,1,1,1,1,1},new[]{true,true},new[]{true,true}};
    if(Fault=="scalar_magnitude")raw[3]=new double[]{1,1};if(Fault=="short_tangents")raw[6]=new double[3];if(Fault=="flag_numeric")raw[4]=new[]{1,1};return Alter(raw);
   }
   public void Dispose(){}
  }
  static Dictionary<string,object> Samples(RotationMeasurements.Reading reading){var s=reading.Samples;return new Dictionary<string,object>{{"type",s.Type},{"nativeId",s.NativeId},{"frame",s.Frame},{"units",s.Units},{"descriptorSha256",s.DescriptorSha256},{"positions",s.Positions.Select(p=>new Dictionary<string,object>{{"key",p.Key},{"value",p.Value}}).ToArray()},{"directions",s.Directions.Select(p=>new Dictionary<string,object>{{"key",p.Key},{"value",p.Value}}).ToArray()},{"boundsWorldRawPaired",s.BoundsPaired}};}
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".rotation-measurement-fixture")),"Explicit own measurement marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string kind in new[]{"surface","curve"})foreach(string fault in new[]{"control","error_code","nonfinite","bool_numeric","array_rank","short_points","flag_numeric","changed_process","mapping_changed","expired","ambiguous_name","invalid_domain",kind=="surface"?"short_normals":"short_tangents",kind=="surface"?"wrong_type":"scalar_magnitude"}) {
    var f=new OwnedOperationPlanTests.Fixture(true,true);var context=new Fake{Snapshot=Clone(f.Snapshot),Listing=Clone(f.Listing),Fault=fault};int[] key=kind=="surface"?new[]{21,8}:new[]{59,10};DateTime expiry=DateTime.UtcNow.AddSeconds(190);
    if(fault=="ambiguous_name"){var entities=EntitySets.Inventory(context.Snapshot).ToList();var alias=Clone(entities.Single(e=>Equals(e["nativeTypeCode"],key[0])));alias["nativeId"]=999;entities.Add(alias);context.Snapshot["entities"]=entities.ToArray();context.Snapshot["entityCount"]=entities.Count;f.Snapshot=Clone(context.Snapshot);}
    if(fault=="expired")expiry=DateTime.UtcNow.AddSeconds(-1);
    if(fault=="wrong_type")key=new[]{40,5};
    var before=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));RotationMeasurements.Reading reading=null;string error=null;
    try{reading=kind=="surface"?RotationMeasurements.Surface(f.Snapshot,key,expiry,context,0,fault=="invalid_domain"?0:1,2,0,1,2):RotationMeasurements.Curve(f.Snapshot,key,expiry,context,0,fault=="invalid_domain"?-1:1);}catch(Exception failure){error=failure.Message;}
    Require((reading!=null)==(fault=="control"),kind+"/"+fault+": unexpected decision "+error);Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Read altered protected files.");
    if(reading!=null){Require(context.Calls==1&&context.Reads==4,"Repeated getter/missing guards.");Require(reading.Samples.Positions.Length==(kind=="surface"?4:2)&&reading.Samples.Directions.Length==4,"Native sample count differs.");}
    bool beforeGetter=new[]{"expired","ambiguous_name","invalid_domain","wrong_type"}.Contains(fault);Require(context.Calls==(beforeGetter?0:1),"Unexpected getter count.");
    rows.Add(new Dictionary<string,object>{{"kind",kind},{"fault",fault},{"root",f.Root},{"getterCalls",context.Calls},{"observationReads",context.Reads},{"descriptor",reading==null?null:reading.Descriptor},{"raw",reading==null?null:reading.Raw},{"samples",reading==null?null:Samples(reading)},{"error",error},{"protectedHashes",before},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","rotation-measurement-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false},{"liveGetterQualified",false}});Console.WriteLine("PASS: "+rows.Count+" passive native measurement guard/layout cases; no CAD.");return rows.Count;
  }
 }
}
