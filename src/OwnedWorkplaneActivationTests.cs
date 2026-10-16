using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneActivationTests {
  static void Need(bool ok,string why){OwnedWorkplanePlans.Need(ok,why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> x){return OwnedWorkplanePlans.Clone(x);}
  internal sealed class Context:OwnedWorkplaneActivation.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int Active,Calls;internal const int Plane=1080000011;
   internal Context(Dictionary<string,object> snapshot,Dictionary<string,object> listing){Snapshot=Clone(snapshot);Listing=Clone(listing);Active=Convert.ToInt32(((System.Collections.IList)Snapshot["contextBefore"])[1]);}
   public Dictionary<string,object> Observation(){
    if(Calls>0&&Fault=="read_throw_after_return")throw new IOException("Injected failed observation after authoritative return.");
    var o=Clone(Snapshot);o["timestampUtc"]=DateTime.UtcNow.ToString("o");o["contextBefore"]=o["contextAfter"]=new[]{0,Active};var body=EntitySets.Inventory(o).Single(e=>Equals(e["nativeTypeCode"],40));var g=(Dictionary<string,object>)body["geometry"];
    g["coordinateFrame"]=Active==0?"world":"active_workplane_unqualified";g["cogRaw"]=Active==0?new[]{0,0,10}:new[]{2,6,-3};
    if(Calls>0&&Fault=="wrong_cog")g["cogRaw"]=new[]{999,0,0};if(Calls>0&&Fault=="body_changed")body["name"]="Changed";
    if(Fault=="selection")o["selectionAfter"]=new[]{1};return o;
   }
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){if(Calls>0&&Fault=="foreign_document")throw new IOException("Injected document change.");}
   public int Composite(int type,int id){return checked(type*20000000+id);}
   public double[] Bounds(int composite){return new[]{-10.0,10.0,-10.0,10.0,0.0,20.0};}
   public int ActiveWorkplane(){return Active;}
   public object WorkplaneIds(){return new[]{Plane};}
   public object WorkplaneInfo(int composite){Need(composite==Plane,"Unexpected workplane mapping.");return new object[]{2,3,4,0,1,0,0,0,1};}
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
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));File.WriteAllText(Path.Combine(Program.Root,"reports","workplane-activation-tests.json"),Program.Json.Serialize(new{state="passed",cadConnectionAttempted=false,cases=rows}));Console.WriteLine("PASS: "+rows.Count+" native activation controls; no CAD connection.");return rows.Count;
  }
 }
}
