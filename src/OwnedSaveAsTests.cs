using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Opaque local fixtures only; never attach PowerShape or execute a macro.
 internal static class OwnedSaveAsTests {
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Fake : OwnedSaveAs.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal int Calls,Reads;internal bool Throw,ThrowAfter,NoFile,WrongContent,Changed;
   internal Action<int> DuringRead;internal Action<string> DuringSave;
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> Observation(){Reads++;if(DuringRead!=null)DuringRead(Reads);return Clone(Snapshot);}
   public void ConfirmDocument(){if(Changed)throw new InvalidOperationException("Synthetic retained SaveAs document changed.");}
   public void SaveAs(string path) {
    Calls++;if(DuringSave!=null)DuringSave(path);if(Throw)throw new IOException("Synthetic command return lost.");
    if(!NoFile)File.WriteAllText(path,"Opaque synthetic saved result; never CAD.");
    var model=(Dictionary<string,object>)Snapshot["model"];model["path"]=path;model["name"]=Path.GetFileNameWithoutExtension(path);
    var window=Window();window["modelPath"]=path;window["modelName"]=model["name"];Listing["activeWindow"]=window;
    if(WrongContent)Snapshot["lengthUnitFactorRaw"]=2;
    if(ThrowAfter)throw new IOException("Synthetic lost SaveAs return after fake effect.");
   }
   public void Dispose(){}
   internal Dictionary<string,object> Window(){return (Dictionary<string,object>)((IList)Listing["windows"])[0];}
  }
  internal sealed class Case {
   internal readonly OwnedOperationPlanTests.Fixture Fixture=new OwnedOperationPlanTests.Fixture(true,true);
   internal readonly Dictionary<string,object> Parameters,Target;internal readonly string TargetId,CopyId,ResultPath,Checkpoint,WorkingSha,CheckpointSha;
   internal Case() {
    TargetId=Guid.NewGuid().ToString("N");var prepared=OwnedSaveTargets.Prepare(Fixture.Root,TargetId,new Dictionary<string,object>{{"bindingId",Fixture.BindingId},{"bindingSha256",Fixture.Parameters["bindingSha256"]}});
    Target=(Dictionary<string,object>)prepared["target"];CopyId=(string)Target["copyId"];ResultPath=(string)Target["resultPath"];Checkpoint=(string)Target["checkpointPath"];
    WorkingSha=Program.Hash(Fixture.Working);CheckpointSha=Program.Hash(Checkpoint);
    Parameters=new Dictionary<string,object>{{"targetId",TargetId},{"targetSha256",prepared["targetSha256"]}};
   }
   internal Fake Context(){return new Fake{Snapshot=Clone(Fixture.Snapshot),Listing=Clone(Fixture.Listing)};}
   internal Dictionary<string,object> Execute(Fake context,string id=null){return OwnedSaveAs.Execute(Fixture.Root,id??Guid.NewGuid().ToString("N"),Parameters,context);}
   internal bool Protected(){return Program.Hash(Fixture.Working)==WorkingSha&&Program.Hash(Checkpoint)==CheckpointSha;}
   internal string Fence{get{return OwnedTranslation.FencePath(Fixture.Root,CopyId);}}
   internal string Claim{get{return Path.Combine(OwnedSaveTargets.Folder(Fixture.Root,TargetId),"save-once.json");}}
  }
  internal static int Run() {
   int count=0;Action<bool> check=value=>{if(!value)throw new Exception("Owned SaveAs synthetic test failed at "+count);count++;};
   var successful=new Case();var context=successful.Context();string id=Guid.NewGuid().ToString("N");var result=successful.Execute(context,id);
   check(Equals(result["state"],"owned_save_as_executed_unverified")&&context.Calls==1&&context.Reads==6);
   check(Equals(result["cadSaveAttempted"],true)&&Equals(result["geometryMutationAttempted"],false)&&Equals(result["saveCommandReturned"],true));
   check(File.Exists(successful.Fence)&&File.Exists(successful.Claim)&&successful.Protected());
   check(NativeJournal.ForVerification(successful.Fixture.Root,id,(string)result["planSha256"]).State=="executed_unverified");
   var replay=successful.Context();check(Equals(successful.Execute(replay)["state"],"owned_save_as_rejected_before_change")&&replay.Calls==0);
   check(EntitySets.Canonical(OwnedSaveAs.SavedProof(successful.ResultPath))==EntitySets.Canonical(result["savedResult"]));
   foreach(string fault in new[]{"throw","no_file","wrong_content","changed_context","wrong_window","changed_working","checkpoint_write","wrong_model_name","active_listing_mismatch"}) {
    var test=new Case();var fake=test.Context();fake.Throw=fault=="throw";fake.NoFile=fault=="no_file";fake.WrongContent=fault=="wrong_content";
    if(fault=="changed_context")fake.DuringSave=path=>fake.Changed=true;
    if(fault=="wrong_window")fake.DuringSave=path=>fake.Window()["nativeWindowId"]=77;
    if(fault=="changed_working")fake.DuringSave=path=>File.AppendAllText(test.Fixture.Working,"synthetic external saved-byte change");
    if(fault=="checkpoint_write")fake.DuringSave=path=>File.AppendAllText(test.Checkpoint,"synthetic forbidden checkpoint write");
    if(fault=="wrong_model_name")fake.DuringRead=n=>{if(n==5)fake.Window()["modelName"]="Another saved document";};
    if(fault=="active_listing_mismatch")fake.DuringRead=n=>{if(n==5){var active=Clone(fake.Window());active["syntheticUnexpectedMetadata"]="different";fake.Listing["activeWindow"]=active;}};
    var failure=test.Execute(fake);check(Equals(failure["state"],"owned_save_as_outcome_unknown")&&fake.Calls==1&&File.Exists(test.Fence)&&File.Exists(test.Claim));
    var retry=test.Context();check(Equals(test.Execute(retry)["state"],"owned_save_as_rejected_before_change")&&retry.Calls==0);
    check(Program.Hash(test.Checkpoint)==test.CheckpointSha&&(fault=="changed_working"?!test.Protected():test.Protected()));
   }
   foreach(string fault in new[]{"target_file","target_directory","name_collision","tampered_binding","foreign_process","retained_context","extra_argument"}) {
    var test=new Case();var fake=test.Context();
    if(fault=="target_file")File.WriteAllText(test.ResultPath,"synthetic existing result");
    if(fault=="target_directory")Directory.CreateDirectory(test.ResultPath);
    if(fault=="name_collision")fake.Window()["modelName"]=Path.GetFileNameWithoutExtension(test.ResultPath);
    if(fault=="tampered_binding")File.AppendAllText(Path.Combine(test.Fixture.Root,"documentbindings",test.Fixture.BindingId+".json")," ");
    if(fault=="foreign_process")fake.Snapshot["processId"]=99;
    if(fault=="retained_context")fake.Changed=true;
    if(fault=="extra_argument")test.Parameters["resultPath"]=test.Fixture.Working;
    var refused=test.Execute(fake);check(Equals(refused["state"],"owned_save_as_rejected_before_change")&&fake.Calls==0&&!File.Exists(test.Fence)&&!File.Exists(test.Claim)&&test.Protected());
   }
   var late=new Case();var lateFake=late.Context();lateFake.DuringRead=n=>{if(n==3)lateFake.Snapshot["processId"]=99;};
   var lateResult=late.Execute(lateFake);check(Equals(lateResult["state"],"owned_save_as_rejected_before_change")&&lateFake.Calls==0&&Equals(lateResult["recordedJournalState"],"rejected_before_change")&&!lateResult.ContainsKey("journalError"));
   check(!File.Exists(late.Fence)&&File.Exists(late.Claim)&&late.Protected());
   var lateRetry=late.Context();check(Equals(late.Execute(lateRetry)["state"],"owned_save_as_rejected_before_change")&&lateRetry.Calls==0);
   var verifiedCase=new Case();var verifiedContext=verifiedCase.Context();var executed=verifiedCase.Execute(verifiedContext);var proof=Proof(verifiedCase,executed,verifiedContext);
   var verified=OwnedSaveVerification.Verify(verifiedCase.Fixture.Root,proof,verifiedContext);
   check(Equals(verified["state"],"owned_save_as_verified_observed")&&verifiedContext.Calls==1&&!File.Exists(verifiedCase.Fence)&&File.Exists(verifiedCase.Claim)&&verifiedCase.Protected());
   string terminalPath=Path.Combine(verifiedCase.Fixture.Root,"operations",(string)executed["operationId"],"000005.json"),terminalSha=Program.Hash(terminalPath);
   check(Equals(OwnedSaveVerification.Verify(verifiedCase.Fixture.Root,proof,verifiedContext)["state"],"owned_save_as_verified_observed")&&Program.Hash(terminalPath)==terminalSha&&verifiedContext.Calls==1);
   string verifiedId=Guid.NewGuid().ToString("N");verified["bridgeRequestId"]=verifiedId;
   string verifiedSha=NativeJournal.Freeze(Path.Combine(verifiedCase.Fixture.Root,"responses",verifiedId+".json"),verified);
   NativeJournal.Freeze(Path.Combine(verifiedCase.Fixture.Root,"saved-result-fixture.json"),new Dictionary<string,object>{{"verificationRequestId",verifiedId},{"verificationReceiptSha256",verifiedSha},{"parameters",proof}});
   var afterVerifyRetry=verifiedCase.Context();check(Equals(verifiedCase.Execute(afterVerifyRetry)["state"],"owned_save_as_rejected_before_change")&&afterVerifyRetry.Calls==0);
   foreach(string fault in new[]{"stale_read","wrong_window_choice","changed_result","wrong_units","foreign_process","changed_document","wrong_fence","missing_fence","partial_journal","forged_result_sha"}) {
    var test=new Case();var fake=test.Context();var execution=test.Execute(fake);var verification=Proof(test,execution,fake);
    if(fault=="stale_read") {
     var old=Clone(fake.Snapshot);old["timestampUtc"]=test.Fixture.Snapshot["timestampUtc"];string oldId=Guid.NewGuid().ToString("N");verification["inspectionRequestId"]=oldId;verification["inspectionSha256"]=NativeJournal.Freeze(Path.Combine(test.Fixture.Root,"responses",oldId+".json"),old);
    }
    if(fault=="wrong_window_choice")verification["windowNumber"]=2;
    if(fault=="changed_result")File.AppendAllText(test.ResultPath,"synthetic changed saved result");
    if(fault=="wrong_units")fake.Snapshot["lengthUnitFactorRaw"]=2;
    if(fault=="foreign_process")fake.Snapshot["processId"]=99;
    if(fault=="changed_document")fake.Changed=true;
    if(fault=="wrong_fence")File.AppendAllText(test.Fence," ");
    if(fault=="missing_fence")File.Delete(test.Fence);
    if(fault=="partial_journal")File.WriteAllText(Path.Combine(test.Fixture.Root,"operations",(string)execution["operationId"],"writer.lock"),"synthetic interrupted writer");
    if(fault=="forged_result_sha")verification["saveReceiptSha256"]=new string('f',64);
    var rejection=OwnedSaveVerification.Verify(test.Fixture.Root,verification,fake);
    check(Equals(rejection["state"],"owned_save_as_verification_rejected")&&fake.Calls==1&&test.Protected());
    check(fault=="missing_fence"?!File.Exists(test.Fence):File.Exists(test.Fence));
    check(!File.Exists(Path.Combine(test.Fixture.Root,"operations",(string)execution["operationId"],"000005.json")));
   }
   var thrownCase=new Case();var thrownContext=thrownCase.Context();thrownContext.Throw=true;var thrown=thrownCase.Execute(thrownContext);
   // A fabricated response return flag cannot supply a missing durable return.
   thrown["saveCommandReturned"]=true;var thrownProof=Proof(thrownCase,thrown,thrownContext);
   check(Equals(OwnedSaveVerification.Verify(thrownCase.Fixture.Root,thrownProof,thrownContext)["state"],"owned_save_as_verification_rejected")&&File.Exists(thrownCase.Fence)&&thrownContext.Calls==1);
   var config=Program.ReadConfig();var missing=Program.Json.Deserialize<Config>(Program.Json.Serialize(config));missing.ownedSavesExperimental=true;missing.ownedSaveTargetsExperimental=false;
   bool dependencyRejected=false;try{missing.Validate();}catch(ArgumentException){dependencyRejected=true;}check(dependencyRejected);
   foreach(bool verify in new[]{false,true}) {
    var invalid=OwnedSaveAs.Run(Guid.NewGuid().ToString("N"),new Dictionary<string,object>(),verify);
    check(Equals(invalid["powerShapeConnectionAttempted"],false)&&Equals(invalid["cadSaveAttempted"],false));
   }
   foreach(string recipe in new[]{"prepare_owned_save_as","execute_owned_save_as","verify_owned_save_as"}) {
    object parameters=recipe=="prepare_owned_save_as"?(object)new Dictionary<string,object>{{"bindingId",successful.Fixture.BindingId},{"bindingSha256",successful.Fixture.Parameters["bindingSha256"]}}:recipe=="execute_owned_save_as"?(object)successful.Parameters:proof;
    bool admitted=false;try{FileRequests.Parse(new string('d',32),Program.Json.Serialize(new{requestId=new string('d',32),operation="recipe.prepare",arguments=new{recipeId=recipe,parameters=parameters}}));admitted=true;}catch(ArgumentException){}
    check(admitted==(recipe=="prepare_owned_save_as"?config.ownedSaveTargetsExperimental:config.ownedSavesExperimental));
   }
   return count;
  }
  internal static Dictionary<string,object> Proof(Case test,Dictionary<string,object> receipt,Fake context) {
   string id=(string)receipt["operationId"],receiptSha=NativeJournal.Freeze(Path.Combine(test.Fixture.Root,"responses",id+".json"),receipt);
   var snapshot=Clone(context.Snapshot);snapshot["timestampUtc"]=DateTime.UtcNow.AddMilliseconds(1).ToString("o");
   string inspectionId=Guid.NewGuid().ToString("N"),inspectionSha=NativeJournal.Freeze(Path.Combine(test.Fixture.Root,"responses",inspectionId+".json"),snapshot);
   var listing=Clone(context.Listing);listing["state"]="documents_listed_verified";listing["timestampUtc"]=DateTime.UtcNow.AddMilliseconds(1).ToString("o");
   string listingId=Guid.NewGuid().ToString("N"),listingSha=NativeJournal.Freeze(Path.Combine(test.Fixture.Root,"responses",listingId+".json"),listing);var active=(Dictionary<string,object>)listing["activeWindow"];
   return new Dictionary<string,object>{{"saveRequestId",id},{"saveReceiptSha256",receiptSha},{"inspectionRequestId",inspectionId},{"inspectionSha256",inspectionSha},{"listingRequestId",listingId},{"listingSha256",listingSha},{"modelId",active["modelId"]},{"windowNumber",active["windowNumber"]},{"expiresUtc",DateTime.UtcNow.AddSeconds(100).ToString("o")}};
  }
 }
}
