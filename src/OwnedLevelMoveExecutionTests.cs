using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedLevelMoveExecutionTests {
  static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Context : OwnedLevelMoveExecution.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault,WorkingPath;internal bool Moved,DoubleFlags;internal int Calls;internal double BoundShift;
   internal Context(OwnedOperationPlanTests.Fixture fixture){WorkingPath=fixture.Working;Snapshot=Clone(fixture.Snapshot);Listing=Clone(fixture.Listing);Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","General"},{"activeRaw",1}}};}
   internal Context(Dictionary<string,object> snapshot,Dictionary<string,object> listing,string working){WorkingPath=working;Snapshot=Clone(snapshot);Listing=Clone(listing);}
   public Dictionary<string,object> Observation(){if(Moved&&Fault=="post_read_error"){Fault=null;throw new IOException("Own injected lost post-read.");}var result=Clone(Snapshot);result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){if(Fault=="retained")throw new IOException("Own retained context refusal.");}
   public object LevelFilters(){return new[]{0,0,Moved&&Fault=="filter_change"?1:0};}
   public object LevelFields(){
    var names=Enumerable.Repeat((object)"",1000).ToArray();var used=Enumerable.Repeat((object)0,1000).ToArray();var active=Enumerable.Repeat((object)0,1000).ToArray();
    names[0]="General";names[5]=Moved&&Fault=="level_name"?"Changed":"Named unused";active[0]=1;
    foreach(var entity in EntitySets.Inventory(Snapshot))used[(int)entity["levelNumber"]]=1;
    if(DoubleFlags){used=used.Select(value=>(object)(double)(int)value).ToArray();active=active.Select(value=>(object)(double)(int)value).ToArray();}
    return new object[]{names,used,active};
   }
   public object LevelSample(int number){var data=(object[])LevelFields();return new[]{((object[])data[0])[number],((object[])data[1])[number],((object[])data[2])[number]};}
   public int Composite(int type,int id){Need(type==40&&id==5,"Wrong execution target.");return Moved&&Fault=="mapping_change"?800000006:800000005;}
   public double[] Bounds(int composite){Need(composite==800000005||composite==800000006,"Wrong composite.");return new[]{-10+BoundShift,10+BoundShift,-10.0,10.0,0.0,20.0};}
   public object SetItemsLevel(int composite,int level) {
    Calls++;Need(composite==800000005&&level==5,"Unplanned native call.");if(Fault=="throw_before")throw new IOException("Own exception before simulated mutation.");if(Fault=="no_op")return true;
    Moved=true;var entities=EntitySets.Inventory(Snapshot);entities.Single(item=>Equals(item["nativeTypeCode"],40))["levelNumber"]=Fault=="wrong_level"?6:level;
    if(Fault=="foreign_object")entities.Single(item=>Equals(item["nativeTypeCode"],21))["name"]="Unplanned";
    if(Fault!="selection_preserved"){Snapshot["contextBefore"]=Snapshot["contextAfter"]=new[]{0,Fault=="workplane_change"?12:0};Snapshot["selectionBefore"]=Snapshot["selectionAfter"]=new object[0];}
    if(Fault=="geometry_change")((Dictionary<string,object>)entities.Single(item=>Equals(item["nativeTypeCode"],40))["geometry"])["volumeRaw"]=99;
    if(Fault=="unit_change")Snapshot["lengthUnitFactorRaw"]=2;
    if(Fault=="bad_bounds")BoundShift=1;
    if(Fault=="windows_change")((Dictionary<string,object>)Listing["activeWindow"])["nativeWindowId"]=99;
    Snapshot["levelsContainingEntities"]=entities.Select(item=>(int)item["levelNumber"]).Distinct().OrderBy(number=>number).Select(number=>{var row=(object[])LevelSample(number);return new Dictionary<string,object>{{"number",number},{"name",row[0]},{"activeRaw",row[2]}};}).ToArray();
    if(Fault=="throw_after")throw new IOException("Own exception after simulated mutation.");
    if(Fault=="saved_change")using(var writer=new FileStream(WorkingPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))writer.SetLength(writer.Length+1);
    if(Fault=="return_false"||Fault=="receipt_forged_success")return false;if(Fault=="return_int")return 1;if(Fault=="return_null")return null;return true;
   }
   public void Dispose(){}
  }
  static Dictionary<string,object> Prepare(OwnedOperationPlanTests.Fixture fixture,Context context) {
   string root=fixture.Root,sourceId=Guid.NewGuid().ToString("N"),expires=DateTime.UtcNow.AddSeconds(240).ToString("o");context.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");context.Snapshot["bridgeRequestId"]=sourceId;
   string sourceSha=NativeJournal.Freeze(Path.Combine(root,"responses",sourceId+".json"),context.Snapshot);
   var original=DocumentBindings.Load(root,new Dictionary<string,object>{{"bindingId",fixture.BindingId},{"bindingSha256",fixture.Parameters["bindingSha256"]}});string bindingId=Guid.NewGuid().ToString("N");
   var binding=DocumentBindings.Prepare(root,bindingId,new Dictionary<string,object>{{"copyId",original["copyId"]},{"ownershipSha256",original["ownershipSha256"]},{"inspectionRequestId",sourceId},{"inspectionSha256",sourceSha},{"expiresUtc",expires}},context.Snapshot,context,new Dictionary<string,object>());
   string setId=Guid.NewGuid().ToString("N"),setSha=NativeJournal.Freeze(Path.Combine(root,"entitysets",setId+".json"),new Dictionary<string,object>{{"inspectionRequestId",sourceId},{"inspectionSha256",sourceSha},{"expiresUtc",expires},{"entityKeys",new[]{new[]{40,5}}}});
   string levelId=Guid.NewGuid().ToString("N");var levels=NativeLevelReading.Read(Clone(context.Snapshot),EntitySets.Expiry(expires),context);levels["bridgeRequestId"]=levelId;Need(Equals(levels["state"],"native_levels_read_unqualified"),"Initial native fixture read failed.");string levelSha=NativeJournal.Freeze(Path.Combine(root,"responses",levelId+".json"),levels);
   string id=Guid.NewGuid().ToString("N");var prepared=OwnedLevelMovePlans.Check(root,id,new Dictionary<string,object>{{"entitySetId",setId},{"entitySetSha256",setSha},{"bindingId",bindingId},{"bindingSha256",binding["bindingSha256"]},{"levelInspectionRequestId",levelId},{"levelInspectionSha256",levelSha},{"parameters",new Dictionary<string,object>{{"targetLevelNumber",5},{"targetLevelName","Named unused"},{"targetActiveRaw",0},{"selectionAfterPolicy","cleared_by_documented_api"},{"mode","move_in_place"}}}},true,context);
   Need(Equals(prepared["state"],"owned_level_move_prepared_observed"),"Execution fixture preparation failed: "+Program.Json.Serialize(prepared));return new Dictionary<string,object>{{"ownedLevelMoveId",id},{"ownedLevelMoveSha256",prepared["ownedLevelMoveSha256"]}};
  }
  internal static int Run() {
   Need(File.Exists(Path.Combine(Program.Root,".owned-level-move-execution-fixture")),"Isolated level executor fixture marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","double_flags","held_writer","retained","throw_before","throw_after","return_false","return_int","return_null","no_op","wrong_level","foreign_object","selection_preserved","geometry_change","unit_change","workplane_change","bad_bounds","windows_change","mapping_change","level_name","filter_change","post_read_error","missing_response","receipt_forged_success","fence_tamper","missing_fence","stale_inspection","stale_listing","stale_levels","level_receipt_mismatch","capture_receipt","expired_verifier","journal_tamper","replay_after_verify","saved_change","held_fence","busy_verifier","claim_exists","existing_journal","preexisting_fence","claim_tamper","missing_claim"}) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);var context=new Context(fixture){DoubleFlags=fault=="double_flags"};context.Snapshot["contextBefore"]=context.Snapshot["contextAfter"]=new[]{1,0};context.Snapshot["selectionBefore"]=context.Snapshot["selectionAfter"]=new[]{new Dictionary<string,object>{{"type","Solid"},{"name","Synthetic_40"}}};
    var handle=Prepare(fixture,context);context.Fault=fault;string id=Guid.NewGuid().ToString("N");FileStream writer=null;
    if(fault=="claim_exists")File.WriteAllText(Path.Combine(fixture.Root,"ownedlevelmoves",(string)handle["ownedLevelMoveId"]+".execution-once.json"),"Existing own claim.");
    if(fault=="existing_journal")Directory.CreateDirectory(Path.Combine(fixture.Root,"operations",id));
    if(fault=="preexisting_fence")File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Working),"mutation-fence.json"),"Existing own unresolved fence.");
    var before=Hashes(fixture.Root);
    if(fault=="held_writer")writer=new FileStream(fixture.Working,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
    Dictionary<string,object> execution;try{execution=OwnedLevelMoveExecution.Execute(fixture.Root,id,handle,context);}finally{if(writer!=null)writer.Dispose();}
    bool rejected=new[]{"retained","claim_exists","existing_journal","preexisting_fence"}.Contains(fault);bool unknown=new[]{"throw_before","throw_after","return_false","return_int","return_null","no_op","wrong_level","foreign_object","selection_preserved","geometry_change","unit_change","workplane_change","bad_bounds","windows_change","mapping_change","level_name","filter_change","post_read_error","receipt_forged_success","saved_change"}.Contains(fault);
    string expected=rejected?"owned_level_move_rejected_before_change":unknown?"owned_level_move_outcome_unknown":"owned_level_move_executed_unverified";
    Need(Equals(execution["state"],expected),fault+": "+Program.Json.Serialize(execution));Need(context.Calls==(rejected?0:1),"Unexpected mutation callback count.");Need(before.All(pair=>fault=="saved_change"&&pair.Key==fixture.Working||Program.Hash(pair.Key)==pair.Value),"Execution changed protected fixture bytes.");
    Directory.CreateDirectory(Path.Combine(fixture.Root,"reports"));string resultPath=Path.Combine(fixture.Root,"reports","execution-result.json"),resultSha=NativeJournal.Freeze(resultPath,execution);
    var retry=OwnedLevelMoveExecution.Execute(fixture.Root,rejected?id:Guid.NewGuid().ToString("N"),handle,context);Need(Equals(retry["state"],"owned_level_move_rejected_before_change")&&context.Calls==(rejected?0:1),"Claimed intent or identical failed request replay reached mutation callback.");
    Dictionary<string,object> verification=null,followup=null,proof=null;int verifyCalls=0;
    if(!rejected) {
     var receipt=Clone(execution);if(fault=="receipt_forged_success"){receipt["returnCode"]=1;receipt["nativeReturnType"]="System.Boolean";}
     string receiptSha=fault=="missing_response"?new string('a',64):NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",id+".json"),receipt);
     var independent=new Context(fixture){Snapshot=Clone(context.Snapshot),Listing=Clone(context.Listing),Moved=context.Moved,DoubleFlags=context.DoubleFlags,BoundShift=context.BoundShift};
     if(new[]{"mapping_change","filter_change","level_name"}.Contains(fault))independent.Fault=fault;
     // Independent reader has no injected faults or mutation calls.
     var observed=independent.Observation();if(fault=="stale_inspection")observed["timestampUtc"]=DateTime.UtcNow.AddMinutes(-1).ToString("o");if(fault=="capture_receipt")observed["viewportCapture"]=new Dictionary<string,object>();
     string observedId=Guid.NewGuid().ToString("N"),observedSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",observedId+".json"),observed);
     var listing=independent.Windows();listing["state"]="documents_listed_verified";listing["timestampUtc"]=DateTime.UtcNow.AddMinutes(fault=="stale_listing"?-1:0).ToString("o");string listingId=Guid.NewGuid().ToString("N"),listingSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",listingId+".json"),listing);
     var levels=NativeLevelReading.Read(independent.Observation(),DateTime.UtcNow.AddSeconds(90),independent);string levelId=Guid.NewGuid().ToString("N");levels["bridgeRequestId"]=levelId;if(fault=="stale_levels")levels["timestampUtc"]=DateTime.UtcNow.AddMinutes(-1).ToString("o");if(fault=="level_receipt_mismatch")((Dictionary<string,object>)((IList)levels["levelSlots"])[5])["name"]="Forged";
     string levelSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",levelId+".json"),levels);
     string fence=OwnedTranslation.FencePath(fixture.Root,(string)execution["copyId"]);if(fault=="fence_tamper")File.AppendAllText(fence,"tamper");if(fault=="missing_fence")File.Move(fence,fence+".held-by-fixture");if(fault=="journal_tamper")File.AppendAllText(Path.Combine(fixture.Root,"operations",id,"000003.json"),"tamper");
     string claim=Path.Combine(fixture.Root,"ownedlevelmoves",(string)handle["ownedLevelMoveId"]+".execution-once.json");if(fault=="claim_tamper")File.AppendAllText(claim,"tamper");if(fault=="missing_claim")File.Move(claim,claim+".held-by-fixture");
     proof=new Dictionary<string,object>{{"executionRequestId",id},{"executionReceiptSha256",receiptSha},{"inspectionRequestId",observedId},{"inspectionSha256",observedSha},{"listingRequestId",listingId},{"listingSha256",listingSha},{"levelInspectionRequestId",levelId},{"levelInspectionSha256",levelSha},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(fault=="expired_verifier"?-1:60).ToString("o")}};
     FileStream heldFence=null,busy=null;
     if(fault=="held_fence")heldFence=new FileStream(fence,FileMode.Open,FileAccess.Read,FileShare.Read);
     if(fault=="busy_verifier")busy=new FileStream(Path.Combine(fixture.Root,"operations","executor.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
     try{verification=OwnedLevelMoveExecution.Verify(fixture.Root,proof,independent);}finally{if(heldFence!=null)heldFence.Dispose();if(busy!=null)busy.Dispose();}verifyCalls=independent.Calls;
     bool accepted=new[]{"control","double_flags","held_writer","post_read_error","replay_after_verify"}.Contains(fault);
     Need(Equals(verification["state"],accepted?"owned_level_move_verified_observed":"owned_level_move_verification_rejected"),fault+" verification: "+Program.Json.Serialize(verification));Need(independent.Calls==0,"Independent verifier invoked mutation.");
     if(accepted){Need(!File.Exists(fence),"Verified own fence not released.");var again=OwnedLevelMoveExecution.Verify(fixture.Root,proof,independent);Need(Equals(again["state"],"owned_level_move_verified_observed")&&independent.Calls==0,"Verification recheck failed.");}
     else if(fault!="missing_fence")Need(File.Exists(fence),"Failed verification cleared fence.");
     if(fault=="held_fence") {Need(Equals(verification["state"],"owned_level_move_verification_rejected"),"Held fence unexpectedly released.");followup=OwnedLevelMoveExecution.Verify(fixture.Root,proof,independent);Need(Equals(followup["state"],"owned_level_move_verified_observed")&&!File.Exists(fence)&&independent.Calls==0,"Interrupted metadata cleanup did not reverify without command replay.");}
     if(fault=="replay_after_verify") {var old=new Context(fixture){Snapshot=Clone((Dictionary<string,object>)((Dictionary<string,object>)execution["plan"])["before"])};var repeated=OwnedLevelMoveExecution.Execute(fixture.Root,Guid.NewGuid().ToString("N"),handle,old);Need(Equals(repeated["state"],"owned_level_move_rejected_before_change")&&old.Calls==0,"Consumed intent replayed after context restoration.");}
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",fixture.Root},{"requestId",id},{"executionResultPath",resultPath},{"executionResultSha256",resultSha},{"executionState",execution["state"]},{"calls",context.Calls},{"verificationCalls",verifyCalls},{"verification",verification},{"followupVerification",followup},{"verificationArguments",proof},{"protectedHashes",before},{"artifactHashes",Hashes(fixture.Root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-level-move-execution-tests.json"),new Dictionary<string,object>{{"state","passed"},{"schema","psbridge.owned-level-move-execution-matrix.v1"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" owned level move executor/independent verifier cases; fake callbacks/no CAD.");return rows.Count;
  }
 }
}
