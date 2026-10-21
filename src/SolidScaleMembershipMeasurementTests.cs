using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class SolidScaleMembershipMeasurementTests {
  static void Need(bool value,string why){if(!value)throw new InvalidOperationException(why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Context : SolidFaceMeasurements.IContext {
   internal readonly Dictionary<string,object> Snapshot,Listing;internal int ClosestCalls,PointCalls,GetterCalls;internal string Fault;double scale=1;readonly double[] centre={2,3,4};
   internal Context(){var f=new SolidFaceMeasurementTests.Context();Snapshot=Clone(f.Snapshot);Listing=Clone(f.Listing);Snapshot["entities"]=new[]{EntitySets.Inventory(Snapshot)[0]};Snapshot["entityCount"]=1;Snapshot["busy"]=false;EntitySets.Inventory(Snapshot)[0]["description"]="Synthetic solid";Snapshot["areaUnitNameRaw"]="unknown area units";Snapshot["areaUnitFactorRaw"]="unknown area units";Snapshot["volumeUnitNameRaw"]="cl";Snapshot["volumeUnitFactorRaw"]=0.0001;}
   public Dictionary<string,object> Observation(){GetterCalls++;return Clone(Snapshot);}
   public Dictionary<string,object> Windows(){GetterCalls++;return Clone(Listing);}
   public void ConfirmDocument(){GetterCalls++;if(Fault=="document")throw new InvalidOperationException("Synthetic retained document changed.");}
   public int Composite(int type,int id){GetterCalls++;Need(type==40&&id==5,"Foreign explicit target.");return Fault=="mapping"&&PointCalls>0?41:40;}
   double[] Transform(double[] p){return Enumerable.Range(0,3).Select(i=>centre[i]+scale*(p[i]-centre[i])).ToArray();}
   public double[] Bounds(int composite){GetterCalls++;Need(composite==40||composite==41,"Foreign composite.");var lo=Transform(new double[]{0,0,0});var hi=Transform(new double[]{10,20,30});return Enumerable.Range(0,6).Select(i=>(i%2==0?lo:hi)[i/2]).ToArray();}
   public object ClosestSolidFace(int id,double[] point){ClosestCalls++;Need(id==5,"Foreign target.");int mask=0;var mid=Transform(new double[]{5,10,15});for(int i=0;i<3;i++)if(point[i]>mid[i])mask|=1<<i;return (scale==1?"source-":"scaled-")+mask;}
   public object FaceNear(string face,double[] point){return new double[]{int.Parse(face.Substring(face.LastIndexOf('-')+1)),0,0};}
   public object FacePoint(string face,double t,double u,bool normal){
    Need(!normal,"Scale membership adapter must not request normal preservation.");PointCalls++;int mask=(int)t;var p=Transform(new[]{(mask&1)==0?0.0:10,(mask&2)==0?0.0:20,(mask&4)==0?0.0:30});
    if(Fault=="outside")p[0]+=100;if(Fault=="mismatch")for(int i=0;i<3;i++)p[i]+=0.007;
    if(Fault=="context")Snapshot["processId"]=99;if(Fault=="windows")Listing["windowCount"]=2;return p;
   }
   internal void ApplyScale(double factor){scale=factor;var g=(Dictionary<string,object>)EntitySets.Inventory(Snapshot)[0]["geometry"];g["cogRaw"]=Transform(new double[]{5,10,15});g["areaRaw"]=2400*factor*factor;g["volumeRaw"]=0.8*factor*factor*factor;}
   public void Dispose(){}
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".solid-scale-measurement-fixture")),"Explicit own fake measurement fixture marker required; never attach CAD.");
   int checks=0;Action<bool> check=ok=>{Need(ok,"Scale measurement check failed: "+checks);checks++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(ArgumentException){rejected=true;}check(rejected);};
   foreach(double factor in new[]{0.5,2.0}){
    var c=new Context();var expected=c.Observation();string windows=DocumentBindings.WindowDigest(c.Windows());var source=SolidScaleMembershipMeasurements.CaptureSource(expected,5,windows,c);var before=SolidScaleMembershipMeasurements.Source(Clone(source),Clone(expected));
    check(c.ClosestCalls==8&&c.PointCalls==8&&before.DocumentToleranceMm==0.01);c.ApplyScale(factor);
    var result=SolidScaleMembershipMeasurements.MeasureTarget(Clone(source),Clone(expected),factor,new double[]{2,3,4},c);
    check(result.Verification.MaximumMembershipDistanceMm==0&&result.Verification.ChangedObservedFaceNames==8&&c.ClosestCalls==16&&c.PointCalls==16);
   }
   foreach(string fault in new[]{"context","windows","mapping","outside","mismatch","document"}){
    var c=new Context();var expected=c.Observation();var source=SolidScaleMembershipMeasurements.CaptureSource(expected,5,DocumentBindings.WindowDigest(c.Windows()),c);c.ApplyScale(0.5);c.Fault=fault;rejects(()=>SolidScaleMembershipMeasurements.MeasureTarget(source,expected,0.5,new double[]{2,3,4},c));
   }
   foreach(string fault in new[]{"units","tolerance","health","selection","busy","multiple"}){
    var c=new Context();if(fault=="units")c.Snapshot["lengthUnitFactorRaw"]=25.4;if(fault=="tolerance")c.Snapshot["generalToleranceRaw"]=0.02;if(fault=="health")((Dictionary<string,object>)EntitySets.Inventory(c.Snapshot)[0]["geometry"])["closedRaw"]=0;if(fault=="selection")c.Snapshot["contextBefore"]=new[]{1,0};if(fault=="busy")c.Snapshot["busy"]=true;if(fault=="multiple")c.Snapshot["entityCount"]=2;
    rejects(()=>SolidScaleMembershipMeasurements.CaptureSource(c.Observation(),5,DocumentBindings.WindowDigest(c.Windows()),c));check(c.ClosestCalls==0);
   }
   var stable=new Context();var pinned=stable.Observation();var bundle=SolidScaleMembershipMeasurements.CaptureSource(pinned,5,DocumentBindings.WindowDigest(stable.Windows()),stable);
   foreach(string fault in new[]{"source","digest","key","profile","extra"}){
    var bad=Clone(bundle);if(fault=="source")bad["sourceObservationSha256"]=new string('a',64);if(fault=="digest")bad["sourceWitnessSha256"]=new string('a',64);if(fault=="key")bad["entityKey"]=new[]{40,6};if(fault=="profile")bad["profile"]="rotation";if(fault=="extra")bad["executionAuthority"]=true;int calls=stable.GetterCalls;
    rejects(()=>SolidScaleMembershipMeasurements.MeasureTarget(bad,pinned,0.5,new double[]{2,3,4},stable));check(calls==stable.GetterCalls);
   }
   foreach(double factor in new[]{double.NaN,double.PositiveInfinity,0.0,1.0,101.0,1.000000001}){int calls=stable.GetterCalls;rejects(()=>SolidScaleMembershipMeasurements.MeasureTarget(bundle,pinned,factor,new double[]{2,3,4},stable));check(calls==stable.GetterCalls);}
   var wrongUnits=new Context();var original=wrongUnits.Observation();var unitsBundle=SolidScaleMembershipMeasurements.CaptureSource(original,5,DocumentBindings.WindowDigest(wrongUnits.Windows()),wrongUnits);wrongUnits.ApplyScale(0.5);wrongUnits.Snapshot["areaUnitFactorRaw"]=1;int faceCalls=wrongUnits.ClosestCalls;rejects(()=>SolidScaleMembershipMeasurements.MeasureTarget(unitsBundle,original,0.5,new double[]{2,3,4},wrongUnits));check(faceCalls==wrongUnits.ClosestCalls);
   Console.WriteLine("PASS: "+checks+" Native scale membership adapter checks; fake getters only, no CAD.");return checks;
  }
 }
}
