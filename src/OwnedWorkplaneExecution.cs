using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShape.Shared;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneExecution {
  static void Need(bool ok,string why){OwnedWorkplanePlans.Need(ok,why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return OwnedWorkplanePlans.Clone(value);}
  static string Artifact(string root,string folder,object id){return OwnedWorkplanePlans.Artifact(root,folder,id);}
  static Dictionary<string,object> Result(string state){return new Dictionary<string,object>{{"state",state},{"bridgeVersion",Program.ReleaseVersion},{"cadMutationAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"nativeProviderQualified",false},{"retryAllowed",false}};}
  internal static Dictionary<string,object> Postconditions(Dictionary<string,object> plan,Dictionary<string,object> after,OwnedWorkplanePlans.IReadContext context) {
   var before=(Dictionary<string,object>)plan["before"];var old=EntitySets.Inventory(before);var fresh=EntitySets.Inventory(after);
   Need(fresh.Length==old.Length+1,"Creation must add exactly one entity.");
   var added=fresh.Where(e=>!old.Any(p=>Equals(p["nativeTypeCode"],e["nativeTypeCode"])&&Equals(p["nativeId"],e["nativeId"]))).ToArray();
   Need(added.Length==1&&Equals(added[0]["nativeTypeCode"],54),"Exactly one new native WORKPLANE required.");
   int native=(int)added[0]["nativeId"],composite=context.Composite(54,native);
   Need(composite==(int)plan["createdCompositeId"],"New workplane mapping differs from native return.");
   OwnedWorkplanePlans.WorldEmpty(after,context);
   var normalized=Clone(after);var remaining=EntitySets.Inventory(normalized).Where(e=>!Equals(e["nativeTypeCode"],54)||!Equals(e["nativeId"],native)).ToArray();
   normalized["entities"]=remaining;normalized["entityCount"]=remaining.Length;normalized["countsByType"]=remaining.GroupBy(e=>(string)e["type"]).ToDictionary(g=>g.Key,g=>g.Count());
   normalized["workplanes"]=((IEnumerable)normalized["workplanes"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).Where(wp=>!Equals(wp["nativeId"],native)).ToArray();
   var levels=old.Select(e=>(int)e["levelNumber"]).Distinct().ToArray();
   normalized["levelsContainingEntities"]=((IEnumerable)normalized["levelsContainingEntities"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).Where(row=>levels.Contains((int)row["number"])).ToArray();
   EntitySets.CheckObserved(before,normalized,new int[0][],DateTime.UtcNow.AddSeconds(30),DateTime.UtcNow);
   var expectedFrame=OwnedWorkplanePlans.Frame((Dictionary<string,object>)plan["parameters"]);var actual=OwnedWorkplanePlans.Info(context.WorkplaneInfo(composite));
   Need(WorkplaneFrame.Same(expectedFrame,actual),"Created world origin/Z/Y differ from requested values.");
   var priorFrames=((IEnumerable)plan["framesBefore"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).ToArray();
   var frameRead=OwnedWorkplanePlans.Frames(after,context);var allFrames=((IEnumerable)frameRead["frames"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).ToArray();
   Need(allFrames.Length==priorFrames.Length+1,"Unexpected workplane getter count.");
   foreach(var prior in priorFrames) {
    var match=allFrames.Single(v=>Equals(v["nativeId"],prior["nativeId"]));Need(EntitySets.Canonical(prior)==EntitySets.Canonical(match),"Existing workplane changed.");
   }
   foreach(var item in ((IEnumerable)plan["boundsBeforeWorldRaw"]).Cast<object>().Select(v=>(Dictionary<string,object>)v)) {
    int id=context.Composite((int)item["nativeTypeCode"],(int)item["nativeId"]);Need(id==(int)item["compositeId"],"Existing entity mapping changed.");
    LevelMovePostconditions.Bounds(((IEnumerable)item["boundsWorldRaw"]).Cast<object>().Select(Program.StrictDouble).ToArray(),context.Bounds(id));
   }
   return new Dictionary<string,object>{{"nativeId",native},{"compositeId",composite},{"nativeTypeCode",54},{"worldInfo",actual},{"levelNumber",added[0]["levelNumber"]},{"name",added[0]["name"]},{"activeWorkplaneAfter",0},{"layout","origin XYZ; Z vector; Y vector"},{"lengthUnits","document_raw"}};
  }
  static void SavedUnchanged(Dictionary<string,object> plan) {
   var prior=plan["savedFileHashesBefore"] as IDictionary;Need(prior!=null,"Recorded saved-file map required.");var current=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["beforeWindows"]);
   Need(current.Count==prior.Count&&current.All(p=>prior.Contains(p.Key)&&Equals(prior[p.Key],p.Value)),"Original saved document bytes changed.");
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,OwnedWorkplanePlans.IContext context) {
   var result=Result("owned_workplane_rejected_before_change");NativeJournal journal=null;bool attempted=false;string fence=null;
   try {
    var checkedResult=OwnedWorkplanePlans.Check(root,id,args,false,context,(packet,before,lease,proofs)=>{
     var parameters=(Dictionary<string,object>)((Dictionary<string,object>)packet["arguments"])["parameters"];var frame=OwnedWorkplanePlans.Frame(parameters);
     var windows=context.Windows();Need(DocumentBindings.WindowDigest(windows)==(string)packet["windowContextSha256"],"Workplane window context differs.");
     var frameRead=OwnedWorkplanePlans.Frames(before,context);
     string claim=Path.Combine(root,"ownedworkplanes",(string)args["ownedWorkplaneId"]+".execution-once.json");
     Need(!File.Exists(claim)&&!Directory.Exists(claim),"Workplane intent already claimed; replay forbidden.");
     var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"operation","create_owned_world_workplane"},{"ownedWorkplaneId",args["ownedWorkplaneId"]},{"ownedWorkplaneSha256",args["ownedWorkplaneSha256"]},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"workingSha256",packet["workingSha256"]},{"parameters",parameters},{"before",before},{"beforeWindows",windows},{"windowContextSha256",packet["windowContextSha256"]},{"savedFileHashesBefore",WorkingCopyOpen.SavedHashes(windows)},{"boundsBeforeWorldRaw",OwnedWorkplanePlans.Bounds(before,context)},{"framesBefore",frameRead["frames"]},{"emptyGetterNullBeforeUnqualified",frameRead["nullWithEmptyInventoryUnqualified"]},{"createdUtc",DateTime.UtcNow.ToString("o")}};
     string planPath=Artifact(root,"workplaneexecutions",id);Directory.CreateDirectory(Path.GetDirectoryName(planPath));string sha=NativeJournal.Freeze(planPath,plan);
     result["operationId"]=id;result["plan"]=plan;result["planSha256"]=sha;result["copyId"]=packet["copyId"];
     journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"operation","create_owned_world_workplane"}});
     fence=OwnedTranslation.FencePath(root,(string)packet["copyId"]);string fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"automaticReplayForbidden",true}});result["executionFenceSha256"]=fenceSha;
     string claimSha=NativeJournal.Freeze(claim,new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"ownedWorkplaneId",args["ownedWorkplaneId"]},{"copyId",packet["copyId"]},{"automaticReplayForbidden",true}});result["claimSha256"]=claimSha;
     proofs.Json(fence,fenceSha);proofs.Json(claim,claimSha);journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"ownedWorkplaneId",args["ownedWorkplaneId"]}});
     DateTime expiry=EntitySets.Expiry(packet["expiresUtc"]);lease.Recheck();context.ConfirmDocument();EntitySets.CheckObserved(before,context.Observation(),new int[0][],expiry,DateTime.UtcNow);OwnedWorkplanePlans.WorldEmpty(before,context);SavedUnchanged(plan);
     Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Workplane context changed at command boundary.");
     journal.Append("command_started",new Dictionary<string,object>{{"command","direct COM CreateWorkplane, then declared ActiveWorkplane=0 only after positive return"},{"maximumCreateCalls",1},{"maximumWorldActivationCalls",1}});
     context.ConfirmDocument();attempted=true;result["cadMutationAttempted"]=result["geometryMutationAttempted"]=result["contextChangeAttempted"]=true;
     result["createCalls"]=1;object raw=context.CreateWorkplane(frame);result["createReturnType"]=raw==null?"null":raw.GetType().FullName;
     Need(raw is int&&(int)raw>0,"CreateWorkplane must return a positive native Int32; no retry or world activation after an unproven return.");
     int created=(int)raw,active=context.ActiveWorkplane();Need(active>0,"Created workplane autoactivation not observed; no further context command.");
     var creation=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"createdCompositeId",created},{"nativeReturnType","System.Int32"},{"createdActiveRaw",active},{"createCalls",1},{"timestampUtc",DateTime.UtcNow.ToString("o")}};
     string creationPath=Path.Combine(root,"workplaneexecutions",id+".creation-return.json");string creationSha=NativeJournal.Freeze(creationPath,creation);result["creationReceiptSha256"]=creationSha;result["createdCompositeId"]=created;
     // This is a declared second step, not recovery/Undo after an unknown creation.
     context.ConfirmDocument();Need(DateTime.UtcNow<expiry,"Lifetime expired before declared world activation.");result["worldActivationCalls"]=1;context.ActivateWorld();Need(context.ActiveWorkplane()==0,"Declared world activation did not verify.");
     journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id},{"createdCompositeId",created},{"nativeReturnType","System.Int32"},{"worldActivationReturned",true},{"creationReceiptSha256",creationSha}});
     var augmented=Clone(plan);augmented["createdCompositeId"]=created;
     for(int pass=0;pass<2;pass++){var after=context.Observation();result["createdWorkplane"]=Postconditions(augmented,after,context);result["after"]=Clone(after);}
     lease.Recheck();context.ConfirmDocument();SavedUnchanged(plan);Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Post-workplane context changed.");
     result["commandCalls"]=2;result["worldActivationReturned"]=true;result["sameWorkerPostconditionsPassed"]=true;result["copyFenced"]=true;result["savedFilesUnchanged"]=true;result["state"]="owned_workplane_executed_unverified";
     journal.Append("executed_unverified",new Dictionary<string,object>{{"independentVerificationRequired",true},{"createdCompositeId",created}});
    });
    Need(Equals(checkedResult["state"],"owned_workplane_validated_observed"),checkedResult.ContainsKey("error")?(string)checkedResult["error"]:"Owned workplane guard rejected.");
   }catch(Exception error) {
    bool unknown=attempted||journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State);result["state"]=unknown?"owned_workplane_outcome_unknown":"owned_workplane_rejected_before_change";result["error"]=error.Message;
    if(journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State))try{journal.Append("outcome_unknown",new Dictionary<string,object>{{"cadMutationAttempted",attempted},{"error",error.Message}});}catch(Exception failure){result["journalError"]=failure.Message;}
   }
   result["recordedJournalState"]=journal==null?null:journal.State;result["copyFenced"]=fence!=null&&(File.Exists(fence)||Directory.Exists(fence));result["timestampUtc"]=DateTime.UtcNow.ToString("o");
   result["qualification"]="One new world workplane with declared automatic activation then return to world; existing observed properties/bounds preserved. No save/reopen or full topology claim. Never replay an unknown outcome.";return result;
  }
  internal static Dictionary<string,object> Verify(string root,Dictionary<string,object> args,OwnedWorkplanePlans.IReadContext context) {
   var result=Result("owned_workplane_verification_rejected");OwnedOperationPlans.Proofs fenceProofs=null;
   try {
    OwnedTranslation.ValidateVerification(args);string id=(string)args["executionRequestId"];
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["executionReceiptSha256"],16000000);
     Need(Equals(receipt["operationId"],id)&&new[]{"owned_workplane_executed_unverified","owned_workplane_outcome_unknown"}.Contains((string)receipt["state"])&&Equals(receipt["cadSaveAttempted"],false),"Original attempted workplane execution receipt required.");
     var plan=proofs.Json(Artifact(root,"workplaneexecutions",id),(string)receipt["planSha256"],16000000);Need(EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"])&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"create_owned_world_workplane"),"Frozen workplane execution differs.");
     var journal=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]);var returned=journal.CommandReturnedDetails;
     OwnedWorkplanePlans.Fields(returned,"nativeRequestId","createdCompositeId","nativeReturnType","worldActivationReturned","creationReceiptSha256");
     Need(Equals(returned["nativeRequestId"],id)&&returned["createdCompositeId"] is int&&(int)returned["createdCompositeId"]>0&&Equals(returned["nativeReturnType"],"System.Int32")&&Equals(returned["worldActivationReturned"],true),"Authoritative creation and declared world-activation returns required.");
     var creation=proofs.Json(Path.Combine(root,"workplaneexecutions",id+".creation-return.json"),(string)returned["creationReceiptSha256"]);
     OwnedWorkplanePlans.Fields(creation,"schemaVersion","operationId","planSha256","createdCompositeId","nativeReturnType","createdActiveRaw","createCalls","timestampUtc");
     Need(Equals(creation["schemaVersion"],1)&&creation["createdActiveRaw"] is int&&(int)creation["createdActiveRaw"]>0&&Equals(creation["operationId"],id)&&Equals(creation["planSha256"],receipt["planSha256"])&&Equals(creation["createdCompositeId"],returned["createdCompositeId"])&&Equals(creation["nativeReturnType"],"System.Int32")&&Equals(creation["createCalls"],1),"Original creation-return proof differs.");
     var claim=proofs.Json(Path.Combine(root,"ownedworkplanes",(string)plan["ownedWorkplaneId"]+".execution-once.json"),(string)receipt["claimSha256"]);
     OwnedWorkplanePlans.Fields(claim,"operationId","planSha256","ownedWorkplaneId","copyId","automaticReplayForbidden");Need(Equals(claim["operationId"],id)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["ownedWorkplaneId"],plan["ownedWorkplaneId"])&&Equals(claim["copyId"],plan["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Workplane once claim differs.");
     var observed=proofs.Json(Artifact(root,"responses",args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Artifact(root,"responses",args["listingRequestId"]),(string)args["listingSha256"],16000000);DateTime expiry=EntitySets.Expiry(args["expiresUtc"]);
     EntitySets.CheckObserved(observed,observed,new int[0][],expiry,DateTime.UtcNow);Need(Equals(listing["state"],"documents_listed_verified")&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Independent workplane listing differs.");
     var active=(Dictionary<string,object>)listing["activeWindow"];Need(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"]),"Independent choice is not the active owned document.");
     foreach(var source in new[]{observed,listing}){var stamp=EntitySets.Expiry(source["timestampUtc"]);Need(stamp>journal.CommandReturnedUtc&&stamp<=DateTime.UtcNow.AddSeconds(5)&&expiry>stamp&&expiry<=stamp.AddSeconds(300),"Independent workplane reads are stale or precede return.");}
     using(var lease=OwnedCopies.Hold(root,(string)plan["copyId"],(string)plan["ownershipSha256"])) {
      OwnedGeometryExportTargets.NoExportFence(root,(string)plan["copyId"]);Need(Equals(lease.Inspection["workingSha256"],plan["workingSha256"]),"Original working saved bytes changed.");
      string fence=OwnedTranslation.FencePath(root,(string)plan["copyId"]);bool exists=File.Exists(fence);
      if(exists){fenceProofs=new OwnedOperationPlans.Proofs();var f=fenceProofs.Json(fence,(string)receipt["executionFenceSha256"]);Need(f.Count==6&&Equals(f["schemaVersion"],1)&&Equals(f["operationId"],id)&&Equals(f["planSha256"],receipt["planSha256"])&&Equals(f["copyId"],plan["copyId"])&&Equals(f["ownershipSha256"],plan["ownershipSha256"])&&Equals(f["automaticReplayForbidden"],true),"Foreign/tampered workplane fence retained.");}
      else Need(journal.State=="succeeded_verified"&&!Directory.Exists(fence),"Unverified workplane fence is missing.");
      DocumentBindings.Check(lease,observed,expiry,context,result,(string)plan["windowContextSha256"]);var augmented=Clone(plan);augmented["createdCompositeId"]=returned["createdCompositeId"];
      for(int pass=0;pass<2;pass++){var fresh=context.Observation();EntitySets.CheckObserved(observed,fresh,new int[0][],expiry,DateTime.UtcNow);result["createdWorkplane"]=Postconditions(augmented,fresh,context);}
      SavedUnchanged(plan);lease.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Independent workplane final context changed.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"createdCompositeId",returned["createdCompositeId"]},{"verificationScope","one new observed workplane/world frame and existing entity properties/world bounds; not saved/reopened or complete topology"}});
      if(exists){fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fence,(string)receipt["executionFenceSha256"],65536);File.Delete(fence);}
      result["state"]="owned_workplane_verified_observed";result["executionOperationId"]=id;result["copyFenceReleased"]=true;result["recordedJournalState"]=journal.State;result["noCommandReplay"]=true;result["evidence"]=evidence;
     }
    }
   }catch(Exception error){result["error"]=error.Message;result["qualification"]="No replay, world activation, inverse, fence cleanup or proof repair after a rejected verification.";}
   finally{if(fenceProofs!=null)fenceProofs.Dispose();}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
