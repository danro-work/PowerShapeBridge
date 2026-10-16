using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneActivationTests {
  static void Need(bool ok,string why){OwnedWorkplanePlans.Need(ok,why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> x){return OwnedWorkplanePlans.Clone(x);}
  internal sealed class Context:OwnedWorkplaneActivation.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int Active,Calls;internal const int Plane=1080000011,Plane2=1080000012,Plane3=1080000013;
   internal Context(Dictionary<string,object> snapshot,Dictionary<string,object> listing){Snapshot=Clone(snapshot);Listing=Clone(listing);Active=Convert.ToInt32(((System.Collections.IList)Snapshot["contextBefore"])[1]);}
   public Dictionary<string,object> Observation(){
    if(Calls>0&&Fault=="read_throw_after_return")throw new IOException("Injected failed observation after authoritative return.");
    var o=Clone(Snapshot);o["timestampUtc"]=DateTime.UtcNow.ToString("o");o["contextBefore"]=o["contextAfter"]=new[]{0,Active};var body=EntitySets.Inventory(o).Single(e=>Equals(e["nativeTypeCode"],40));var g=(Dictionary<string,object>)body["geometry"];
    g["coordinateFrame"]=Active==0?"world":"active_workplane_unqualified";g["cogRaw"]=Active==0?new[]{0,0,10}:Active==Plane?new[]{2,6,-3}:Active==Plane2?new[]{-4,-5,4}:new[]{-9,2,7};
    if(Calls>0&&Fault=="wrong_cog")g["cogRaw"]=new[]{999,0,0};if(Calls>0&&Fault=="body_changed")body["name"]="Changed";
    if(Fault=="selection")o["selectionAfter"]=new[]{1};return o;
   }
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){if(Calls>0&&Fault=="foreign_document")throw new IOException("Injected document change.");}
   public int Composite(int type,int id){return checked(type*20000000+id);}
   public double[] Bounds(int composite){return new[]{-10.0,10.0,-10.0,10.0,0.0,20.0};}
   public int ActiveWorkplane(){return Active;}
   public object WorkplaneIds(){return EntitySets.Inventory(Snapshot).Where(e=>Equals(e["nativeTypeCode"],54)).Select(e=>Composite(54,(int)e["nativeId"])).ToArray();}
   public object WorkplaneInfo(int composite){
    Need(((int[])WorkplaneIds()).Contains(composite),"Unexpected workplane mapping.");
    object[] frame=composite==Plane?new object[]{2,3,4,0,1,0,0,0,1}:composite==Plane2?new object[]{-4,5,6,1,0,0,0,1,0}:new object[]{9,-2,3,0,0,1,0,1,0};
    if(Calls>0&&Fault=="inactive_frame_changed"&&composite==Plane3||Fault=="frame_before_command"&&composite==Plane2)frame[0]=(int)frame[0]+1;return frame;
   }
   public void ActivateWorkplane(int composite){Calls++;Active=composite;if(Fault=="throw_after_setter")throw new IOException("Injected ambiguous setter.");if(Fault=="wrong_target")Active=123;}
   public void Dispose(){}
  }
  internal static Context Seed(OwnedOperationPlanTests.Fixture f,bool active){
   var body=Clone(EntitySets.Inventory(f.Snapshot).Single(e=>Equals(e["nativeTypeCode"],40)));
   f.Snapshot["entities"]=new[]{body,new Dictionary<string,object>{{"nativeTypeCode",54},{"nativeId",11},{"type","WORKPLANE"},{"name","SyntheticWorkplane"},{"levelNumber",0},{"geometry",null}}};f.Snapshot["entityCount"]=2;
   f.Snapshot["workplanes"]=new[]{new Dictionary<string,object>{{"nativeId",11},{"originRaw",new[]{2,3,4}},{"xaxisRaw",new[]{-1,0,0}},{"zaxisRaw",new[]{0,1,0}},{"yaxisRaw",new[]{0,0,1}}}};
   var c=new Context(f.Snapshot,f.Listing);c.Active=active?Context.Plane:0;f.Snapshot=c.Observation();return Rebind(f,c);
  }
  internal static Context Rebind(OwnedOperationPlanTests.Fixture f,Context c){
   var snapshot=c.Observation();f.SourceId=Guid.NewGuid().ToString("N");snapshot["bridgeRequestId"]=f.SourceId;f.Snapshot=Clone(snapshot);c.Snapshot=Clone(snapshot);
   string source=NativeJournal.Freeze(Path.Combine(f.Root,"responses",f.SourceId+".json"),snapshot);f.BindingId=Guid.NewGuid().ToString("N");
   var old=NativeJournal.VerifiedJson(Path.Combine(f.Root,"documentbindings",(string)f.Parameters["bindingId"]+".json"),(string)f.Parameters["bindingSha256"],65536);
   var binding=DocumentBindings.Prepare(f.Root,f.BindingId,new Dictionary<string,object>{{"copyId",old["copyId"]},{"ownershipSha256",old["ownershipSha256"]},{"inspectionRequestId",f.SourceId},{"inspectionSha256",source},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}},snapshot,c,new Dictionary<string,object>());
   f.Parameters["bindingId"]=f.BindingId;f.Parameters["bindingSha256"]=binding["bindingSha256"];return c;
  }
  internal static Context SeedMultiple(OwnedOperationPlanTests.Fixture f,int count=3,int active=0){
   Need(count==2||count==3,"Two or three analytic fixture workplanes required.");var c=Seed(f,false);var entities=EntitySets.Inventory(c.Snapshot).Select(Clone).ToList();
   for(int native=12;native<11+count;native++)entities.Add(new Dictionary<string,object>{{"nativeTypeCode",54},{"nativeId",native},{"type","WORKPLANE"},{"name","SyntheticWorkplane"+native},{"levelNumber",0},{"geometry",null}});
   c.Snapshot["entities"]=entities.ToArray();c.Snapshot["entityCount"]=entities.Count;c.Snapshot["countsByType"]=new Dictionary<string,object>{{"SOLID",1},{"WORKPLANE",count}};
   c.Snapshot["workplanes"]=entities.Where(e=>Equals(e["nativeTypeCode"],54)).Select(e=>{int native=(int)e["nativeId"];var v=((object[])c.WorkplaneInfo(c.Composite(54,native))).Select(Program.StrictDouble).ToArray();return new Dictionary<string,object>{{"nativeId",native},{"originRaw",v.Take(3).ToArray()},{"zaxisRaw",v.Skip(3).Take(3).ToArray()},{"yaxisRaw",v.Skip(6).Take(3).ToArray()}};}).ToArray();
   c.Active=active;return Rebind(f,c);
  }
  internal static Dictionary<string,object> Prepare(OwnedOperationPlanTests.Fixture f,Context c,bool world=false,int native=11){
    return OwnedWorkplaneActivation.Check(f.Root,Guid.NewGuid().ToString("N"),new Dictionary<string,object>{{"bindingId",f.BindingId},{"bindingSha256",f.Parameters["bindingSha256"]},{"parameters",world?new Dictionary<string,object>{{"mode","world"}}:new Dictionary<string,object>{{"mode","existing"},{"nativeId",native}}}},true,c);
  }
  internal static Dictionary<string,object> Verify(string root,string id,string sha,Context c){
   var receipt=NativeJournal.VerifiedJson(Path.Combine(root,"responses",id+".json"),sha,16000000);var returned=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]).CommandReturnedUtc;
   var wait=System.Diagnostics.Stopwatch.StartNew();while(DateTime.UtcNow<=returned&&wait.ElapsedMilliseconds<1000)System.Threading.Thread.Sleep(1);Need(DateTime.UtcNow>returned,"Clock did not advance after return.");
   string observedId=Guid.NewGuid().ToString("N"),listingId=Guid.NewGuid().ToString("N");var o=c.Observation();o["bridgeRequestId"]=observedId;var l=c.Windows();l["state"]="documents_listed_verified";l["timestampUtc"]=DateTime.UtcNow.ToString("o");var active=(Dictionary<string,object>)l["activeWindow"];
   return OwnedWorkplaneActivation.Verify(root,new Dictionary<string,object>{{"executionRequestId",id},{"executionReceiptSha256",sha},{"inspectionRequestId",observedId},{"inspectionSha256",NativeJournal.Freeze(Path.Combine(root,"responses",observedId+".json"),o)},{"listingRequestId",listingId},{"listingSha256",NativeJournal.Freeze(Path.Combine(root,"responses",listingId+".json"),l)},{"expiresUtc",DateTime.UtcNow.AddSeconds(100).ToString("o")},{"windowNumber",active["windowNumber"]},{"modelId",active["modelId"]}},c);
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".workplane-activation-fixture")),"Isolated marker required; never run against installed runtime.");var rows=new List<object>();
   double s=Math.Sqrt(0.5);var tilted=new[]{0.0,0.0,0.0,0.0,0.0,1.0,s,s,0.0};foreach(bool source in new[]{true,false}){bool rejected=false;try{OwnedWorkplaneActivation.ConvertPoint(new[]{1.6e308,1.6e308,1.6e308},source?tilted:null,source?null:tilted);}catch(InvalidDataException){rejected=true;}Need(rejected,"Overflowing COG conversion must refuse, not pass an infinite tolerance.");}
   foreach(string fault in new[]{"control","world","selection","wrong_candidate","already_active","throw_after_setter","wrong_target","wrong_cog","body_changed","foreign_document","read_throw_after_return","foreign_fence","verify_wrong_model","replay"}){
    var f=new OwnedOperationPlanTests.Fixture(true,true);var c=Seed(f,fault=="world");if(fault=="selection")c.Fault=fault;
    var prepared=Prepare(f,c,fault=="world"||fault=="already_active",fault=="wrong_candidate"?99:11);bool pre=new[]{"selection","wrong_candidate","already_active"}.Contains(fault);Dictionary<string,object> execution=null,verification=null;string id=null,sha=null;
    if(pre)Need(Equals(prepared["state"],"owned_workplane_activation_preparation_rejected")&&c.Calls==0,"Unsafe preparation reached setter.");
    else{
     Need(Equals(prepared["state"],"owned_workplane_activation_prepared_observed"),"Preparation failed: "+Program.Json.Serialize(prepared));var args=new Dictionary<string,object>{{"activationId",prepared["activationId"]},{"activationSha256",prepared["activationSha256"]}};id=Guid.NewGuid().ToString("N");c.Fault=fault;
     execution=OwnedWorkplaneActivation.Execute(f.Root,id,args,c);sha=NativeJournal.Freeze(Path.Combine(f.Root,"responses",id+".json"),execution);bool unknown=new[]{"throw_after_setter","wrong_target","wrong_cog","body_changed","foreign_document","read_throw_after_return"}.Contains(fault);
     Need(c.Calls==1&&Equals(execution["state"],unknown?"owned_workplane_activation_outcome_unknown":"owned_workplane_activation_executed_unverified"),"Unexpected execution: "+Program.Json.Serialize(execution));string fence=OwnedTranslation.FencePath(f.Root,(string)execution["copyId"]);Need(File.Exists(fence),"Fence lost before independent verification.");
     if(!unknown||fault=="read_throw_after_return"){
      c.Fault=null;if(fault=="foreign_fence"){var foreign=Clone(NativeJournal.VerifiedJson(fence,(string)execution["executionFenceSha256"],65536));foreign["operationId"]=Guid.NewGuid().ToString("N");File.WriteAllText(fence,Program.Json.Serialize(foreign));}
      if(fault=="verify_wrong_model")((Dictionary<string,object>)c.Snapshot["model"])["id"]=99;
      string held=Program.Hash(fence);verification=Verify(f.Root,id,sha,c);bool accepted=!new[]{"foreign_fence","verify_wrong_model"}.Contains(fault);
      Need(Equals(verification["state"],accepted?"owned_workplane_activation_verified_observed":"owned_workplane_activation_verification_rejected")&&File.Exists(fence)!=accepted&&c.Calls==1,"Verifier changed context or released wrong fence: "+Program.Json.Serialize(verification));if(!accepted)Need(Program.Hash(fence)==held,"Rejected verifier altered fence.");
      if(fault=="replay"){c.Active=0;c.Snapshot=Clone(f.Snapshot);var replay=OwnedWorkplaneActivation.Execute(f.Root,Guid.NewGuid().ToString("N"),args,c);Need(c.Calls==1&&Equals(replay["state"],"owned_workplane_activation_rejected_before_change"),"Consumed intent replayed.");}
     }
    }
    rows.Add(new{fault,root=f.Root,prepared,execution,verification,operationId=id,receiptSha256=sha,setterCalls=c.Calls});
   }
   foreach(var test in new[]{
    new{fault="cross",source=Context.Plane,target=12,count=2},new{fault="reverse",source=Context.Plane2,target=11,count=2},
    new{fault="third",source=Context.Plane3,target=12,count=3},new{fault="world_to_third",source=0,target=13,count=3},
    new{fault="multi_world",source=Context.Plane2,target=0,count=2},new{fault="multi_noop",source=Context.Plane2,target=12,count=2},
    new{fault="multi_wrong_candidate",source=Context.Plane,target=99,count=2},new{fault="inactive_frame_changed",source=Context.Plane,target=12,count=3},
    new{fault="frame_before_command",source=Context.Plane,target=12,count=2}}){
    var f=new OwnedOperationPlanTests.Fixture(true,true);var c=SeedMultiple(f,test.count,test.source);var prepared=Prepare(f,c,test.target==0,test.target);Dictionary<string,object> execution=null,verification=null;
    if(new[]{"multi_noop","multi_wrong_candidate"}.Contains(test.fault))Need(Equals(prepared["state"],"owned_workplane_activation_preparation_rejected")&&c.Calls==0,"Multi preparation did not refuse.");
    else{
     Need(Equals(prepared["state"],"owned_workplane_activation_prepared_observed")&&Equals(((Dictionary<string,object>)prepared["activation"])["schemaVersion"],2),"Multi-frame preparation failed: "+Program.Json.Serialize(prepared));
     c.Fault=test.fault;string id=Guid.NewGuid().ToString("N");execution=OwnedWorkplaneActivation.Execute(f.Root,id,new Dictionary<string,object>{{"activationId",prepared["activationId"]},{"activationSha256",prepared["activationSha256"]}},c);string sha=NativeJournal.Freeze(Path.Combine(f.Root,"responses",id+".json"),execution);
     if(test.fault=="frame_before_command")Need(c.Calls==0&&Equals(execution["state"],"owned_workplane_activation_rejected_before_change"),"Changed frame reached command boundary.");
     else{
      bool changed=test.fault=="inactive_frame_changed";Need(c.Calls==1&&Equals(execution["state"],changed?"owned_workplane_activation_outcome_unknown":"owned_workplane_activation_executed_unverified"),"Cross-frame execution differs: "+Program.Json.Serialize(execution));
      string fence=OwnedTranslation.FencePath(f.Root,(string)execution["copyId"]),held=Program.Hash(fence);verification=Verify(f.Root,id,sha,c);
      Need(c.Calls==1&&Equals(verification["state"],changed?"owned_workplane_activation_verification_rejected":"owned_workplane_activation_verified_observed")&&File.Exists(fence)==changed,"Cross-frame independent result differs: "+Program.Json.Serialize(verification));if(changed)Need(Program.Hash(fence)==held,"Changed inactive frame released fence.");
     }
    }
    rows.Add(new{fault=test.fault,root=f.Root,source=test.source,target=test.target,workplanes=test.count,prepared,execution,verification,setterCalls=c.Calls});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));File.WriteAllText(Path.Combine(Program.Root,"reports","workplane-activation-tests.json"),Program.Json.Serialize(new{state="passed",cadConnectionAttempted=false,cases=rows}));Console.WriteLine("PASS: "+rows.Count+" native activation controls; no CAD connection.");return rows.Count;
  }
 }
}
