using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Explicit offline CLI only. All command contexts below are fake, not COM.
 internal static class RecoveryFaultTests {
  sealed class Case {
   internal string Root,Id=Guid.NewGuid().ToString("N");
   internal Func<Dictionary<string,object>> Execute,Retry;
   internal Func<Dictionary<string,object>,Dictionary<string,object>> Proof,Verify;
   internal Func<int> Calls;internal Action<string> Fault;
   internal string[] Protected;
  }
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static string Checkpoint(string working){return Path.Combine(Path.GetDirectoryName(working),"checkpoint","Before.psmodel");}
  static void Marker(Case c){File.WriteAllText(Path.Combine(c.Root,"operations",c.Id,"writer.lock"),"Synthetic interrupted command-return publication.");}
  static void Changed(Dictionary<string,object> snapshot,Dictionary<string,object> listing){snapshot["processId"]=99;listing["processId"]=99;}
  static Case Translation() {
   var f=new OwnedOperationPlanTests.Fixture(true);var args=OwnedTranslationTests.Handle(f);var ctx=OwnedTranslationTests.Context(f);var c=new Case{Root=f.Root,Protected=new[]{f.Working,Checkpoint(f.Working)}};
   c.Execute=()=>OwnedTranslation.Execute(c.Root,c.Id,args,ctx);c.Retry=()=>OwnedTranslation.Execute(c.Root,Guid.NewGuid().ToString("N"),args,ctx);c.Calls=()=>ctx.Calls;
   c.Proof=receipt=>OwnedTranslationTests.Proof(f,receipt,ctx);c.Verify=proof=>OwnedTranslation.Verify(c.Root,proof,ctx);
   c.Fault=fault=>{if(fault=="lost_return_after_effect")ctx.ThrowAfter=true;if(fault=="non_success")ctx.Code=0;if(fault=="non_success_minus_one")ctx.Code=-1;
    if(fault=="partial_journal")ctx.DuringCommand=()=>Marker(c);if(fault=="changed_context")ctx.DuringCommand=()=>Changed(ctx.Snapshot,ctx.Listing);};return c;
  }
  static Case Save() {
   var f=new OwnedSaveAsTests.Case();var ctx=f.Context();var c=new Case{Root=f.Fixture.Root,Protected=new[]{f.Fixture.Working,f.Checkpoint}};
   c.Execute=()=>f.Execute(ctx,c.Id);c.Retry=()=>f.Execute(ctx);c.Calls=()=>ctx.Calls;c.Proof=receipt=>OwnedSaveAsTests.Proof(f,receipt,ctx);c.Verify=proof=>OwnedSaveVerification.Verify(c.Root,proof,ctx);
   c.Fault=fault=>{if(fault=="lost_return_after_effect")ctx.ThrowAfter=true;if(fault=="non_success")ctx.NoFile=true;
    if(fault=="partial_journal")ctx.DuringSave=path=>Marker(c);if(fault=="changed_context")ctx.DuringSave=path=>Changed(ctx.Snapshot,ctx.Listing);};return c;
  }
  static Case SavedOpen() {
   var f=new OwnedSavedReopenExecutionTests.Case();var ctx=f.Context;var plan=(Dictionary<string,object>)f.Prepared["reopen"];var seed=(Dictionary<string,object>)plan["seed"];
   var c=new Case{Root=f.Root,Protected=new[]{f.Saved.Save.Fixture.Working,f.Saved.Save.Checkpoint,f.Saved.Save.ResultPath,(string)plan["workingPath"],(string)plan["checkpointPath"],(string)seed["path"]}};
   c.Execute=()=>f.Execute(c.Id);c.Retry=()=>f.Execute();c.Calls=()=>ctx.Commands;c.Proof=receipt=>f.Proof(receipt);c.Verify=proof=>OwnedSavedReopenVerification.Verify(c.Root,Guid.NewGuid().ToString("N"),proof,ctx);
   c.Fault=fault=>{if(fault=="lost_return_after_effect")ctx.ThrowAfter=true;if(fault=="non_success")ctx.SuppressOpen=true;
    if(fault=="partial_journal")ctx.DuringOpen=path=>Marker(c);if(fault=="changed_context")ctx.DuringOpen=path=>Changed(ctx.Snapshot,ctx.Listing);};return c;
  }
  static Case Open() {
   var f=new OwnedOperationPlanTests.Fixture(true,true);var c=new Case{Root=f.Root};var copy=OwnedCopies.Create(c.Root,Guid.NewGuid().ToString("N"),f.Working,Program.Hash(f.Working),2);
   var owned=(Dictionary<string,object>)copy["ownership"];var working=(string)((Dictionary<string,object>)owned["working"])["path"];
   c.Protected=new[]{f.Working,Checkpoint(f.Working),working,Checkpoint(working)};
   var ctx=new OwnedSavedReopenExecutionTests.Context{Snapshot=Clone(f.Snapshot),Listing=Clone(f.Listing)};
   var args=new Dictionary<string,object>{{"copyId",copy["copyId"]},{"ownershipSha256",copy["ownershipSha256"]},{"modelId",9},{"windowNumber",1},{"expiresUtc",DateTime.UtcNow.AddSeconds(100).ToString("o")}};
   Reads(c.Root,ctx,args,"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256");
   var expected=EntitySets.Receipt(c.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]);var listing=EntitySets.Receipt(c.Root,(string)args["listingRequestId"],(string)args["listingSha256"]);
   c.Execute=()=>WorkingCopyOpen.Execute(c.Root,c.Id,args,listing,expected,ctx);c.Retry=()=>WorkingCopyOpen.Execute(c.Root,Guid.NewGuid().ToString("N"),args,listing,expected,ctx);c.Calls=()=>ctx.Commands;
   c.Proof=receipt=>{
    var proof=new Dictionary<string,object>{{"openRequestId",c.Id},{"openReceiptSha256",Program.Hash(Path.Combine(c.Root,"responses",c.Id+".json"))},{"modelId",500},{"windowNumber",2},{"expiresUtc",DateTime.UtcNow.AddSeconds(90).ToString("o")}};
    Reads(c.Root,ctx,proof,"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256");return proof;
   };
   c.Verify=proof=>OwnedOpenVerification.Verify(c.Root,proof,()=>ctx);
   c.Fault=fault=>{if(fault=="lost_return_after_effect")ctx.ThrowAfter=true;if(fault=="non_success")ctx.SuppressOpen=true;
    if(fault=="partial_journal")ctx.DuringOpen=path=>Marker(c);if(fault=="changed_context")ctx.DuringOpen=path=>Changed(ctx.Snapshot,ctx.Listing);};return c;
  }
  static void Reads(string root,OwnedSavedReopenExecutionTests.Context ctx,Dictionary<string,object> args,string inspect,string inspectSha,string windows,string windowsSha) {
   string stamp=DateTime.UtcNow.AddMilliseconds(20).ToString("o"),id=Guid.NewGuid().ToString("N"),listId=Guid.NewGuid().ToString("N");
   var snapshot=ctx.Observation();snapshot["timestampUtc"]=stamp;snapshot["bridgeRequestId"]=id;var listing=ctx.Windows();listing["timestampUtc"]=stamp;listing["state"]="documents_listed_verified";
   args[inspect]=id;args[inspectSha]=NativeJournal.Freeze(Path.Combine(root,"responses",id+".json"),snapshot);
   args[windows]=listId;args[windowsSha]=NativeJournal.Freeze(Path.Combine(root,"responses",listId+".json"),listing);
  }
  static Dictionary<string,string> Hashes(string folder){return Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").ToDictionary(p=>p,p=>Program.Hash(p)):new Dictionary<string,string>();}
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  internal static int Run() {
   if(!File.Exists(Path.Combine(Program.Root,".recovery-fault-fixture")))throw new InvalidOperationException("Explicit isolated fault-stage marker required; never run on installed runtime.");
   var rows=new List<Dictionary<string,object>>();
   foreach(string operation in new[]{"translate_owned_solid","save_owned_document_as","open_working_copy","open_owned_saved_result"}) {
    var faults=new List<string>{"control","lost_return_after_effect","lost_response","partial_journal","changed_context","non_success"};if(operation=="translate_owned_solid")faults.Add("non_success_minus_one");
    foreach(string fault in faults) {
     var c=operation=="translate_owned_solid"?Translation():operation=="save_owned_document_as"?Save():operation=="open_working_copy"?Open():SavedOpen();
     var protectedBefore=c.Protected.Distinct().ToDictionary(p=>p,p=>Program.Hash(p));c.Fault(fault);var receipt=c.Execute();receipt["bridgeRequestId"]=c.Id;
     string response=Path.Combine(c.Root,"responses",c.Id+".json");if(operation!="save_owned_document_as"&&!File.Exists(response))NativeJournal.Freeze(response,receipt);
     Require(c.Calls()==1,operation+"/"+fault+": expected one fake command, got "+c.Calls()+"; "+Program.Json.Serialize(receipt));
     string copyId=(string)receipt["copyId"],claim=operation=="translate_owned_solid"?Path.Combine(c.Root,"operationplans",(string)NativeJournal.VerifiedJson(Path.Combine(c.Root,"ownedoperations",(string)((Dictionary<string,object>)receipt["plan"])["ownedOperationId"]+".json"),(string)((Dictionary<string,object>)receipt["plan"])["ownedOperationSha256"],65536)["operationPlanId"]+".execution-once.json"):
      operation=="save_owned_document_as"?Path.Combine(c.Root,"savedresults",(string)((Dictionary<string,object>)receipt["plan"])["targetId"],"save-once.json"):Path.Combine(c.Root,"workingcopies",copyId,"open-once.json");
     string fence=Path.Combine(c.Root,"workingcopies",copyId,"mutation-fence.json"),claimBefore=Program.Hash(claim),fenceBefore=File.Exists(fence)?Program.Hash(fence):null;
     var journalBefore=Hashes(Path.Combine(c.Root,"operations",c.Id));var proof=c.Proof(receipt);
     string retainedResponse=null;
     if(fault=="lost_response") {
      // Hide only this fixture's original reply. Helpers must not recreate it.
      retainedResponse=response+".unpublished";File.Move(response,retainedResponse);
     }
     var verified=c.Verify(proof);
     if(fault=="control")Require(((string)verified["state"]).EndsWith("verified_observed"),operation+": control verification failed: "+Program.Json.Serialize(verified));
     else {
      Require(!((string)verified["state"]).EndsWith("verified_observed"),operation+"/"+fault+": unknown promoted to success.");
      Require(journalBefore.Count==Hashes(Path.Combine(c.Root,"operations",c.Id)).Count&&journalBefore.All(p=>Program.Hash(p.Key)==p.Value),operation+"/"+fault+": verifier modified unresolved journal.");
      Require(File.Exists(claim)&&Program.Hash(claim)==claimBefore,"Unresolved claim changed.");if(fenceBefore!=null)Require(File.Exists(fence)&&Program.Hash(fence)==fenceBefore,"Unresolved fence changed.");
     }
     var retried=c.Retry();Require(c.Calls()==1&&((string)retried["state"]).Contains("reject"),operation+"/"+fault+": duplicate intent executed.");
     Require(protectedBefore.All(p=>Program.Hash(p.Key)==p.Value),operation+"/"+fault+": protected saved bytes changed.");
     rows.Add(new Dictionary<string,object>{{"operation",operation},{"fault",fault},{"root",c.Root},{"operationId",c.Id},{"copyId",copyId},{"commandCalls",c.Calls()},
      {"execution",receipt},{"verification",verified},{"verificationParameters",proof},{"duplicateIntentResult",retried},{"claimPath",claim},{"claimSha256",Program.Hash(claim)},
      {"fencePath",fence},{"fencePresent",File.Exists(fence)},{"journal",Hashes(Path.Combine(c.Root,"operations",c.Id))},{"retainedUnpublishedResponse",retainedResponse},
      {"protectedFiles",protectedBefore.Select(p=>new Dictionary<string,object>{{"path",p.Key},{"sha256",p.Value}}).ToArray()},{"cadConnectionAttempted",false}});
    }
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","recovery-fault-matrix.json"),new Dictionary<string,object>{
    {"schema","psbridge.recovery-fault-matrix.v1"},{"state","native_core_faults_passed"},{"scope","Injected fake native command contexts and own opaque files only; not live CAD or full K7 qualification"},
    {"cadConnectionAttempted",false},{"nativeVersion",Program.ReleaseVersion},{"rows",rows},{"remaining",new[]{"SDK timeout/cancel/lost wire reply per executor","checkpoint recovery accepted scenario","live qualification"}}});
   Console.WriteLine("PASS: "+rows.Count+" native core recovery fault rows; fake contexts only, no CAD.");return rows.Count;
  }
 }
}
