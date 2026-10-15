using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneTests {
  static void Need(bool value,string why){if(!value)throw new InvalidOperationException(why);}
  internal static Dictionary<string,object> Clone(Dictionary<string,object> value){return OwnedWorkplanePlans.Clone(value);}
  internal static Dictionary<string,object> Parameters(){return new Dictionary<string,object>{{"originWorldRaw",new[]{2.0,3.0,4.0}},{"zAxis",new[]{0.0,1.0,0.0}},{"yAxis",new[]{0.0,0.0,1.0}},{"coordinateFrame","world"},{"lengthUnits","document_raw"},{"activeAfter","world"},{"mode","create_new"}};}
  internal sealed class Context : OwnedWorkplanePlans.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int Creates,Activations,Active;internal double[] Frame;
   internal Context(OwnedOperationPlanTests.Fixture fixture){Snapshot=Clone(fixture.Snapshot);Listing=Clone(fixture.Listing);}
   internal Context(Dictionary<string,object> snapshot,Dictionary<string,object> listing){Snapshot=Clone(snapshot);Listing=Clone(listing);}
   public Dictionary<string,object> Observation(){var r=Clone(Snapshot);r["timestampUtc"]=DateTime.UtcNow.ToString("o");return r;}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){}
   public int Composite(int type,int native){return checked(type*20000000+native);}
   public double[] Bounds(int composite){return new[]{-10.0,10.0,-10.0,10.0,0.0,20.0};}
   public int ActiveWorkplane(){return Active;}
   public object WorkplaneIds(){return EntitySets.Inventory(Snapshot).Any(e=>Equals(e["nativeTypeCode"],54))?(object)new[]{1080000011}:null;}
   public object WorkplaneInfo(int composite){Need(composite==1080000011&&Frame!=null,"Unexpected synthetic workplane getter.");var value=Frame.ToArray();if(Fault=="wrong_frame")value[0]+=1;return value.Cast<object>().ToArray();}
   public object CreateWorkplane(double[] frame) {
    Creates++;Frame=frame.ToArray();var entities=EntitySets.Inventory(Snapshot).ToList();
    entities.Add(new Dictionary<string,object>{{"nativeTypeCode",54},{"nativeId",11},{"name","Owned workplane"},{"type","WORKPLANE"},{"levelNumber",0},{"geometry",null}});
    Snapshot["entities"]=entities.ToArray();Snapshot["entityCount"]=entities.Count;Active=1080000011;
    Snapshot["contextBefore"]=Snapshot["contextAfter"]=new[]{0,Active};
    Snapshot["workplanes"]=new[]{new Dictionary<string,object>{{"nativeId",11},{"originRaw",frame.Take(3).ToArray()},{"zaxisRaw",frame.Skip(3).Take(3).ToArray()},{"yaxisRaw",frame.Skip(6).Take(3).ToArray()}}};
    if(Fault=="old_entity_changed")entities[0]["name"]="Changed by injected callback";
    if(Fault=="native_zero")return 0;if(Fault=="native_bool")return true;return 1080000011;
   }
   public void ActivateWorld(){Activations++;Active=0;Snapshot["contextBefore"]=Snapshot["contextAfter"]=new[]{0,0};if(Fault=="world_throw_after")throw new IOException("Injected ambiguous world setter return.");}
   public void Dispose(){}
  }
  internal static Dictionary<string,object> Prepare(OwnedOperationPlanTests.Fixture fixture,Context context,string fault="control") {
   var values=Parameters();if(fault=="nonorthogonal")values["yAxis"]=new[]{0,1,0};if(fault=="bool")values["originWorldRaw"]=new object[]{false,0,0};if(fault=="extra")values["command"]="never accepted";
   var args=new Dictionary<string,object>{{"bindingId",fixture.BindingId},{"bindingSha256",fixture.Parameters["bindingSha256"]},{"parameters",values}};
   return OwnedWorkplanePlans.Check(fixture.Root,Guid.NewGuid().ToString("N"),args,true,context);
  }
  internal static Dictionary<string,object> Verify(string root,string operation,string receiptSha,Context context) {
   // Windows may give adjacent calls the same UTC tick. Generate genuinely
   // later fake read receipts; do not weaken the production after-return guard.
   var original=NativeJournal.VerifiedJson(Path.Combine(root,"responses",operation+".json"),receiptSha,16000000);
   var returned=NativeJournal.ForVerification(root,operation,(string)original["planSha256"]).CommandReturnedUtc;
   var clockWait=System.Diagnostics.Stopwatch.StartNew();
   while(DateTime.UtcNow<=returned&&clockWait.ElapsedMilliseconds<1000)System.Threading.Thread.Sleep(1);
   Need(DateTime.UtcNow>returned,"Synthetic read clock did not advance beyond command return.");
   string source=Guid.NewGuid().ToString("N"),listing=Guid.NewGuid().ToString("N");var observed=context.Observation();observed["bridgeRequestId"]=source;
   var windows=context.Windows();windows["state"]="documents_listed_verified";windows["timestampUtc"]=DateTime.UtcNow.ToString("o");
   string a=NativeJournal.Freeze(Path.Combine(root,"responses",source+".json"),observed),b=NativeJournal.Freeze(Path.Combine(root,"responses",listing+".json"),windows);
   var active=(Dictionary<string,object>)windows["activeWindow"];
   return OwnedWorkplaneExecution.Verify(root,new Dictionary<string,object>{{"executionRequestId",operation},{"executionReceiptSha256",receiptSha},{"inspectionRequestId",source},{"inspectionSha256",a},{"listingRequestId",listing},{"listingSha256",b},{"expiresUtc",DateTime.UtcNow.AddSeconds(120).ToString("o")},{"windowNumber",active["windowNumber"]},{"modelId",active["modelId"]}},context);
  }
  internal static int Run() {
   Need(File.Exists(Path.Combine(Program.Root,".owned-workplane-fixture")),"Isolated workplane test marker required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","nonorthogonal","bool","extra","changed_document","claimed_replay","native_zero","native_bool","world_throw_after","wrong_frame","old_entity_changed","verify_changed_document"}) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);var context=new Context(fixture);
    if(fault=="changed_document")((Dictionary<string,object>)context.Snapshot["model"])["id"]=99;
    var prepared=Prepare(fixture,context,fault);bool preRefusal=new[]{"nonorthogonal","bool","extra","changed_document"}.Contains(fault);
    Dictionary<string,object> execution=null,verification=null,historical=null;string operation=null,receiptSha=null;
    if(preRefusal)Need(Equals(prepared["state"],"owned_workplane_preparation_rejected")&&context.Creates==0&&context.Activations==0,"Unsafe preparation reached mutation.");
    else {
     Need(Equals(prepared["state"],"owned_workplane_prepared_observed"),"Synthetic preparation failed: "+Program.Json.Serialize(prepared));
     var handle=new Dictionary<string,object>{{"ownedWorkplaneId",prepared["ownedWorkplaneId"]},{"ownedWorkplaneSha256",prepared["ownedWorkplaneSha256"]}};operation=Guid.NewGuid().ToString("N");context.Fault=fault;
     execution=OwnedWorkplaneExecution.Execute(fixture.Root,operation,handle,context);receiptSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",operation+".json"),execution);
     bool failed=new[]{"native_zero","native_bool","world_throw_after","wrong_frame","old_entity_changed"}.Contains(fault);
     Need(Equals(execution["state"],failed?"owned_workplane_outcome_unknown":"owned_workplane_executed_unverified"),"Unexpected execution outcome: "+Program.Json.Serialize(execution));
     Need(context.Creates==1&&context.Activations==(new[]{"native_zero","native_bool"}.Contains(fault)?0:1),"Unexpected Create/Activate calls.");
     string fence=OwnedTranslation.FencePath(fixture.Root,(string)execution["copyId"]);Need(File.Exists(fence),"Execution lost matching fence.");
     if(!failed) {
      if(fault=="control") {
       var archived=Clone((Dictionary<string,object>)execution["plan"]);archived["createdCompositeId"]=execution["createdCompositeId"];
       var archivedBefore=(Dictionary<string,object>)archived["before"];archivedBefore["timestampUtc"]=DateTime.UtcNow.AddHours(-2).ToString("o");
       OwnedWorkplaneExecution.Postconditions(archived,context.Observation(),context);
       var changed=context.Observation();EntitySets.Inventory(changed)[0]["name"]="Historical baseline must still reject changed geometry metadata";
       bool rejected=false;try{OwnedWorkplaneExecution.Postconditions(archived,changed,context);}catch(InvalidDataException){rejected=true;}
       Need(rejected&&context.Creates==1&&context.Activations==1,"Historical comparison lost content checking or replayed.");
       Need(Program.Hash(Path.Combine(fixture.Root,"workplaneexecutions",operation+".json"))==(string)execution["planSha256"],"Historical comparison rewrote original evidence.");
       historical=new Dictionary<string,object>{{"beforeTimestampUtc",archivedBefore["timestampUtc"]},{"matchingAccepted",true},{"changedRejected",true},{"originalPlanUnchanged",true}};
      }
      if(fault=="verify_changed_document")((Dictionary<string,object>)context.Snapshot["model"])["id"]=99;
      verification=Verify(fixture.Root,operation,receiptSha,context);bool accepted=fault!="verify_changed_document";
      Need(Equals(verification["state"],accepted?"owned_workplane_verified_observed":"owned_workplane_verification_rejected")&&File.Exists(fence)!=accepted,"Incorrect independent verifier/fence result: "+Program.Json.Serialize(verification));
      Need(context.Creates==1&&context.Activations==1,"Verifier submitted a CAD command.");
      if(fault=="claimed_replay") {
       context.Snapshot=Clone(fixture.Snapshot);context.Active=0;
       var replay=OwnedWorkplaneExecution.Execute(fixture.Root,Guid.NewGuid().ToString("N"),handle,context);
       Need(Equals(replay["state"],"owned_workplane_rejected_before_change")&&context.Creates==1&&context.Activations==1,"Claimed intent replayed.");
      }
     }
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",fixture.Root},{"prepared",prepared},{"execution",execution},{"verification",verification},{"operationId",operation},{"receiptSha256",receiptSha},{"createCalls",context.Creates},{"worldActivationCalls",context.Activations},{"historicalBaselineChecks",historical}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));File.WriteAllText(Path.Combine(Program.Root,"reports","owned-workplane-tests.json"),Program.Json.Serialize(new Dictionary<string,object>{{"state","passed"},{"cases",rows.ToArray()},{"cadConnectionAttempted",false}}));
   Console.WriteLine("PASS: "+rows.Count+" owned workplane controls/faults; fake callbacks, no CAD.");return rows.Count;
  }
 }
}
