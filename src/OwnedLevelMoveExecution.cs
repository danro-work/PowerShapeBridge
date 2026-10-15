using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Native provider implementation exists on ComContext; queue/MCP admission is
 // deliberately absent until source tests and an approved live scenario pass.
 internal static class OwnedLevelMoveExecution {
  internal interface IReadContext : NativeLevelReading.IContext { int Composite(int type,int nativeId); double[] Bounds(int composite); }
  internal interface IContext : IReadContext { object SetItemsLevel(int composite,int level); }
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static string Artifact(string root,string folder,string id){Need(EntitySets.Hex(id,32),"Invalid level execution ID.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  static Dictionary<string,object> Result(string state){return new Dictionary<string,object>{{"state",state},{"bridgeVersion",Program.ReleaseVersion},{"cadMutationAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"nativeProviderQualified",false},{"retryAllowed",false}};}
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> handle,IContext context) {
   var result=Result("owned_level_move_rejected_before_change");NativeJournal journal=null;string fence=null;bool attempted=false;
   try {
    OwnedLevelMovePlans.ValidateArguments(handle,false);Need(EntitySets.Hex(id,32),"Invalid execution request ID.");var submitted=Clone(handle);
    OwnedLevelMovePlans.WithValidated(root,id,submitted,context,(packet,before,levels,lease,proofs)=>{
     int native=EntitySets.Keys(packet["entityKeys"])[0][1];var args=(Dictionary<string,object>)packet["arguments"];var parameters=OwnedLevelMovePlans.Parameters((Dictionary<string,object>)args["parameters"]);int target=(int)parameters["targetLevelNumber"];
     DateTime expires=EntitySets.Expiry(packet["expiresUtc"]);int composite=context.Composite(40,native);var bounds=context.Bounds(composite);LevelMovePostconditions.Bounds(bounds,bounds);
     string claim=Path.Combine(root,"ownedlevelmoves",(string)submitted["ownedLevelMoveId"]+".execution-once.json");OwnedCopies.NoReparse(claim);
     fence=OwnedTranslation.FencePath(root,(string)packet["copyId"]);Need(!File.Exists(claim)&&!Directory.Exists(claim)&&!File.Exists(fence)&&!Directory.Exists(fence),"Level intent already claimed or copy fenced.");
     var windows=context.Windows();Need(DocumentBindings.WindowDigest(windows)==(string)packet["windowContextSha256"],"Execution window context differs.");var saved=WorkingCopyOpen.SavedHashes(windows);
     var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"operation","move_owned_solid_to_level"},{"ownedLevelMoveId",submitted["ownedLevelMoveId"]},{"ownedLevelMoveSha256",submitted["ownedLevelMoveSha256"]},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"workingSha256",packet["workingSha256"]},{"expiresUtc",packet["expiresUtc"]},{"bindingId",args["bindingId"]},{"nativeId",native},{"compositeId",composite},{"targetLevelNumber",target},{"sourceLevelNumber",packet["sourceLevelNumber"]},{"windowContextSha256",packet["windowContextSha256"]},{"boundsBeforeWorldRaw",bounds},{"before",before},{"levelsBefore",levels},{"beforeWindows",windows},{"savedFileHashesBefore",saved},{"createdUtc",DateTime.UtcNow.ToString("o")}};
     string path=Artifact(root,"levelmoveexecutions",id);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,plan);
     result["operationId"]=id;result["planSha256"]=sha;result["plan"]=plan;result["copyId"]=packet["copyId"];result["localArtifactsMayExist"]=true;
     journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"operation","move_owned_solid_to_level"}});
     string fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"automaticReplayForbidden",true}});result["executionFenceSha256"]=fenceSha;
     string claimSha=NativeJournal.Freeze(claim,new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"ownedLevelMoveId",submitted["ownedLevelMoveId"]},{"copyId",packet["copyId"]},{"automaticReplayForbidden",true}});result["claimSha256"]=claimSha;
     proofs.Json(fence,fenceSha);proofs.Json(claim,claimSha);journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"ownedLevelMoveId",submitted["ownedLevelMoveId"]}});
     lease.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expires&&context.Composite(40,native)==composite,"Target mapping or lifetime changed before command.");LevelMovePostconditions.Bounds(bounds,context.Bounds(composite));
     EntitySets.CheckObserved(before,context.Observation(),new[]{new[]{40,native}},expires,DateTime.UtcNow);
     Need(DocumentBindings.WindowDigest(context.Windows())==(string)packet["windowContextSha256"],"Window changed before level command.");
     var currentSaved=WorkingCopyOpen.SavedHashes(windows);Need(saved.Count==currentSaved.Count&&saved.All(pair=>currentSaved[pair.Key]==pair.Value),"Saved document bytes changed before command.");
     journal.Append("command_started",new Dictionary<string,object>{{"command","direct COM SetItemsLevel / one SOLID / explicit named level"},{"selectionAfterPolicy","cleared_by_documented_api"}});
     context.ConfirmDocument();Need(DateTime.UtcNow<expires&&DocumentBindings.WindowDigest(context.Windows())==(string)packet["windowContextSha256"],"Context changed at command boundary.");
     attempted=true;result["cadMutationAttempted"]=true;result["contextChangeAttempted"]=true;object raw=context.SetItemsLevel(composite,target);int code=raw is bool?((bool)raw?1:0):-1;
     result["returnCode"]=code;result["nativeReturnType"]=raw==null?"null":raw.GetType().FullName;
     journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id},{"returnCode",code},{"nativeReturnType",result["nativeReturnType"]}});
     Need(code==1,"SetItemsLevel did not return native Boolean true; no retry or inverse.");
     for(int pass=0;pass<2;pass++) {
      var after=context.Observation();var observedLevels=NativeLevelReading.Read(after,DateTime.UtcNow.AddSeconds(60),context);
      LevelMovePostconditions.Check(before,after,levels,observedLevels,native,target);Need(context.Composite(40,native)==composite,"Target mapping changed after command.");LevelMovePostconditions.Bounds(bounds,context.Bounds(composite));
      result["after"]=Clone(after);result["levelsAfter"]=Clone(observedLevels);
     }
     lease.Recheck();context.ConfirmDocument();Need(DocumentBindings.WindowDigest(context.Windows())==(string)packet["windowContextSha256"],"Window changed after level transfer.");currentSaved=WorkingCopyOpen.SavedHashes(windows);
     Need(saved.Count==currentSaved.Count&&saved.All(pair=>currentSaved[pair.Key]==pair.Value),"Saved document bytes changed during transfer.");
     result["savedFilesUnchanged"]=true;result["commandCalls"]=1;result["sameWorkerPostconditionsPassed"]=true;result["state"]="owned_level_move_executed_unverified";
     journal.Append("executed_unverified",new Dictionary<string,object>{{"sameWorkerPostconditionsPassed",true},{"independentVerificationRequired",true}});
    });
   }catch(Exception error) {
    bool unknown=attempted||journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State);result["state"]=unknown?"owned_level_move_outcome_unknown":"owned_level_move_rejected_before_change";result["error"]=error.Message;
    if(journal!=null)try {
     if(new[]{"command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"cadMutationAttempted",attempted},{"error",error.Message}});
     // Even a known pre-command rejection retains any claim/fence for inspection.
    }catch(Exception failure){result["journalError"]=failure.Message;}
   }
   result["recordedJournalState"]=journal==null?null:journal.State;result["copyFenced"]=fence==null?(object)null:(File.Exists(fence)||Directory.Exists(fence));result["timestampUtc"]=DateTime.UtcNow.ToString("o");
   result["qualification"]="Single observed SOLID membership change in memory; not saved/reopened, no complete topology proof. Independent verification required. No automatic replay, inverse or fence cleanup.";return result;
  }
  internal static Dictionary<string,object> Verify(string root,Dictionary<string,object> arguments,IReadContext context) {
   var result=Result("owned_level_move_verification_rejected");OwnedOperationPlans.Proofs fenceProofs=null;
   try {
    Need(arguments!=null&&arguments.Count==11&&arguments.ContainsKey("levelInspectionRequestId")&&arguments.ContainsKey("levelInspectionSha256")&&EntitySets.Hex(arguments["levelInspectionRequestId"],32)&&EntitySets.Hex(arguments["levelInspectionSha256"],64),"Exact independent level verification arguments required.");
    var args=Clone(arguments);var common=args.Where(pair=>!pair.Key.StartsWith("levelInspection",StringComparison.Ordinal)).ToDictionary(pair=>pair.Key,pair=>pair.Value);OwnedTranslation.ValidateVerification(common);
    string id=(string)args["executionRequestId"];
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["executionReceiptSha256"],16000000);Need(Equals(receipt["operationId"],id)&&new[]{"owned_level_move_executed_unverified","owned_level_move_outcome_unknown"}.Contains((string)receipt["state"])&&Equals(receipt["cadMutationAttempted"],true)&&Equals(receipt["returnCode"],1)&&Equals(receipt["nativeReturnType"],"System.Boolean")&&Equals(receipt["cadSaveAttempted"],false),"Successful native return receipt required.");
     var plan=proofs.Json(Artifact(root,"levelmoveexecutions",id),(string)receipt["planSha256"],16000000);Need(EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"])&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"move_owned_solid_to_level"),"Frozen execution plan differs.");
     string claimPath=Path.Combine(root,"ownedlevelmoves",(string)plan["ownedLevelMoveId"]+".execution-once.json");OwnedCopies.NoReparse(claimPath);var claim=proofs.Json(claimPath,(string)receipt["claimSha256"]);
     Need(claim.Count==5&&Equals(claim["operationId"],id)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["ownedLevelMoveId"],plan["ownedLevelMoveId"])&&Equals(claim["copyId"],plan["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Execution once claim differs; fence retained.");
     var journal=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]);Need(journal.CommandReturnCode==1&&Equals(journal.CommandReturnedDetails["nativeReturnType"],"System.Boolean"),"Authoritative journal return differs.");
     var expected=proofs.Json(Artifact(root,"responses",(string)args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Artifact(root,"responses",(string)args["listingRequestId"]),(string)args["listingSha256"],16000000);
     var independentLevels=proofs.Json(Artifact(root,"responses",(string)args["levelInspectionRequestId"]),(string)args["levelInspectionSha256"],16000000);DateTime expiry=EntitySets.Expiry(args["expiresUtc"]);
     EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
     Need(Equals(independentLevels["state"],"native_levels_read_unqualified")&&Equals(independentLevels["bridgeRequestId"],args["levelInspectionRequestId"]),"Independent level receipt identity differs.");
     foreach(string key in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})Need(Equals(independentLevels[key],false),"Independent level receipt acquired authority.");
     foreach(var snapshot in new[]{expected,listing,independentLevels})Need(!snapshot.ContainsKey("viewportCapture"),"Unexpected capture receipt.");
     EntitySets.CheckObserved(expected,(Dictionary<string,object>)independentLevels["before"],new int[0][],expiry,DateTime.UtcNow);EntitySets.CheckObserved(expected,(Dictionary<string,object>)independentLevels["after"],new int[0][],expiry,DateTime.UtcNow);
     foreach(var proof in new[]{expected,listing,independentLevels}){DateTime time=EntitySets.Expiry(proof["timestampUtc"]);Need(time>journal.CommandReturnedUtc&&time<=DateTime.UtcNow.AddSeconds(5)&&expiry>time&&expiry<=time.AddSeconds(300),"Independent reads must follow command return and have a fresh lease.");}
     Need(Equals(listing["state"],"documents_listed_verified")&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Independent document inventory differs.");var active=(Dictionary<string,object>)listing["activeWindow"];Need(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"]),"Independent active window differs.");
     using(var lease=OwnedCopies.Hold(root,(string)plan["copyId"],(string)plan["ownershipSha256"])) {
      OwnedGeometryExportTargets.NoExportFence(root,(string)plan["copyId"]);
      Need(Equals(plan["workingSha256"],lease.Inspection["workingSha256"]),"Working saved bytes differ.");string fence=OwnedTranslation.FencePath(root,(string)plan["copyId"]);bool exists=File.Exists(fence);
      if(exists){fenceProofs=new OwnedOperationPlans.Proofs();var record=fenceProofs.Json(fence,(string)receipt["executionFenceSha256"]);Need(Equals(record["operationId"],id)&&Equals(record["planSha256"],receipt["planSha256"])&&Equals(record["copyId"],plan["copyId"])&&Equals(record["ownershipSha256"],plan["ownershipSha256"])&&Equals(record["automaticReplayForbidden"],true),"Another or tampered fence must not be released.");}
      else Need(journal.State=="succeeded_verified"&&!Directory.Exists(fence),"Unverified execution fence missing.");
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);int native=(int)plan["nativeId"],target=(int)plan["targetLevelNumber"],composite=context.Composite(40,native);Need(Equals(plan["compositeId"],composite),"Independent target mapping differs from command.");
      var bounds=((System.Collections.IList)plan["boundsBeforeWorldRaw"]).Cast<object>().Select(Program.StrictDouble).ToArray();
      var before=(Dictionary<string,object>)plan["before"];var levelsBefore=(Dictionary<string,object>)plan["levelsBefore"];
      for(int pass=0;pass<2;pass++) {
       var fresh=NativeLevelReading.Read(expected,expiry,context);LevelMovePostconditions.Check(before,expected,levelsBefore,fresh,native,target);LevelMovePostconditions.Bounds(bounds,context.Bounds(composite));
       foreach(string key in new[]{"levelSlots","filterFlagsRaw","filterOrder","windowsBefore","windowsAfter"})Need(EntitySets.Canonical(independentLevels[key])==EntitySets.Canonical(fresh[key]),"Independent level receipt differs from current read.");
      }
      var saved=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["beforeWindows"]);var prior=(Dictionary<string,object>)plan["savedFileHashesBefore"];Need(saved.Count==prior.Count&&saved.All(pair=>prior.ContainsKey(pair.Key)&&Equals(prior[pair.Key],pair.Value)),"An original saved document changed.");
      lease.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Final verification context changed.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}},new Dictionary<string,object>{{"requestId",args["levelInspectionRequestId"]},{"sha256",args["levelInspectionSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"verificationScope","observed in-memory level membership, geometry properties/bounds and other context preserved; not saved/reopened or complete topology"}});
      if(exists){fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fence,(string)receipt["executionFenceSha256"],65536);File.Delete(fence);}
      result["state"]="owned_level_move_verified_observed";result["copyFenceReleased"]=true;result["recordedJournalState"]=journal.State;result["noCommandReplay"]=true;result["executionOperationId"]=id;result["evidence"]=evidence;
     }
    }
   }catch(Exception error){result["error"]=error.Message;result["recoveryInstructions"]="Never replay the command or clear/repair the fence automatically; inspect journal and immutable receipts.";}
   finally{if(fenceProofs!=null)fenceProofs.Dispose();}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
