using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class SolidFaceMeasurementTests {
  static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Context : SolidRotationContext.IWriteContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int ClosestCalls,NearCalls,PointCalls;internal bool Rotated;
   internal Context(){var f=new OwnedOperationPlanTests.Fixture(true,true);Snapshot=Clone(f.Snapshot);Listing=Clone(f.Listing);Snapshot["generalToleranceRaw"]=0.01;var g=(Dictionary<string,object>)EntitySets.Inventory(Snapshot)[0]["geometry"];g["cogRaw"]=new[]{5,10,15};foreach(string k in new[]{"nlinks_open","closedRaw","connectedRaw","watertightRaw","trimming_validRaw"})g[k]=k=="nlinks_open"?0:1;}
   internal int ObservationCalls,WindowCalls,ConfirmationCalls;
   public Dictionary<string,object> Observation(){ObservationCalls++;return Clone(Snapshot);}
   public Dictionary<string,object> Windows(){WindowCalls++;return Clone(Listing);}
   public void ConfirmDocument(){ConfirmationCalls++;}
   public void Dispose(){}
   public int Composite(int type,int id){Need(type==40&&id==5,"Wrong explicit target.");return Fault=="mapping"&&PointCalls>0?999:40;}
   public double[] Bounds(int composite){return Rotated?new double[]{-20,0,0,10,0,30}:new double[]{0,10,0,20,0,30};}
   public object ClosestSolidFace(int id,double[] p){ClosestCalls++;return (double)((p[0]>0?1:0)+(p[1]>0?2:0)+(p[2]>0?4:0));}
   public object FaceNear(string face,double[] point){NearCalls++;return new double[]{int.Parse(face),0,0};}
   public object FacePoint(string face,double t,double u,bool normal){
    Need(int.Parse(face)==(int)t&&u==0,"Pinned UV/name changed.");PointCalls++;
    if(Fault=="context")Snapshot["processId"]=99;
    if(normal)return Fault=="normal"?new double[]{0,0,0}:Rotated?new double[]{0,1,0}:new double[]{1,0,0};
    int mask=(int)t;var p=new[]{(mask&1)!=0?10.0:0,(mask&2)!=0?20.0:0,(mask&4)!=0?30.0:0};if(Fault=="outside")p[0]+=100;
    return Rotated?new[]{-p[1],p[0],p[2]}:p;
   }
   internal void RotateAnalytically(){Rotated=true;((Dictionary<string,object>)EntitySets.Inventory(Snapshot)[0]["geometry"])["cogRaw"]=new[]{-10,5,15};}
   internal int RotateCalls;internal int LastAngle;internal double[] LastAxis;
   public int Rotate(int composite,int angle,double[] axis){RotateCalls++;LastAngle=angle;LastAxis=axis.ToArray();Need(composite==40,"Fake received foreign target.");if(Fault=="throw")throw new IOException("Synthetic COM callback exception.");if(Fault=="zero")return 0;RotateAnalytically();return 1;}
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".solid-face-measurement-fixture")),"Explicit own fixture marker required; never connect CAD.");
   int checks=0;Action<bool> check=yes=>{Need(yes,"Solid witness check failed: "+checks);checks++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(ArgumentException){rejected=true;}catch(FormatException){rejected=true;}check(rejected);};
   var c=new Context();var descriptor=SolidFaceMeasurements.Capture(c.Observation(),5,c);var first=SolidFaceMeasurements.Read(descriptor,c);var again=SolidFaceMeasurements.Read(Clone(descriptor),c);
   check(first.DescriptorSha256==again.DescriptorSha256&&c.ClosestCalls==8&&c.NearCalls==8&&c.PointCalls==32);
   c.RotateAnalytically();var rotated=SolidFaceMeasurements.Read(Clone(descriptor),c);RotationSampleVerifier.Verify(first,rotated,90,new double[]{0,0,0,0,0,1},1e-6,1e-9);check(c.ClosestCalls==8&&c.NearCalls==8&&rotated.Positions.Length==9&&rotated.Directions.Length==8);
   foreach(string fault in new[]{"context","normal","outside","mapping"}){var bad=new Context();var d=SolidFaceMeasurements.Capture(bad.Observation(),5,bad);bad.Fault=fault;rejects(()=>SolidFaceMeasurements.Read(d,bad));}
   var absent=new Context();var absentDescriptor=SolidFaceMeasurements.Capture(absent.Observation(),5,absent);((Dictionary<string,object>)EntitySets.Inventory(absent.Snapshot)[0]["geometry"]).Remove("watertightRaw");rejects(()=>SolidFaceMeasurements.Read(absentDescriptor,absent));
   var unit=new Context();var unitDescriptor=SolidFaceMeasurements.Capture(unit.Observation(),5,unit);unit.Snapshot["lengthUnitFactorRaw"]=25.4;rejects(()=>SolidFaceMeasurements.Read(unitDescriptor,unit));
   var selected=new Context();selected.Snapshot["contextBefore"]=selected.Snapshot["contextAfter"]=new[]{1,0};selected.Snapshot["selectionBefore"]=selected.Snapshot["selectionAfter"]=new[]{new Dictionary<string,object>{{"type","Solid"},{"name","Body"}}};var selectedDescriptor=SolidFaceMeasurements.Capture(selected.Observation(),5,selected);check(SolidFaceMeasurements.Read(selectedDescriptor,selected).Positions.Length==9&&selected.ClosestCalls==8);
   foreach(object invalid in new object[]{true,1.5,double.NaN,double.PositiveInfinity,-1.0,"Hello PowerShape User","x']","x\ny"})rejects(()=>SolidFaceMeasurements.FaceName(invalid));
   check(SolidFaceMeasurements.FaceName(15.0)=="15");rejects(()=>SolidFaceMeasurements.Vector(new object[]{true,0,0}));rejects(()=>SolidFaceMeasurements.Vector(new double[]{1,2}));rejects(()=>SolidFaceMeasurements.Vector(new double[1,3]));
   Console.WriteLine("PASS: "+checks+" solid face provider guards, pinned UV rotation and passive-selection checks; no CAD.");return checks;
  }
 }
}
