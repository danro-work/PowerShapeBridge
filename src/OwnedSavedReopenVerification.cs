using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedSavedReopenVerification {
  internal static void Validate(Dictionary<string,object> args) {
   var common=new Dictionary<string,object>(args);if(!common.ContainsKey("reopenRequestId")||!common.ContainsKey("reopenReceiptSha256"))throw new ArgumentException("Saved reopen verification identity required.");
   common["openRequestId"]=common["reopenRequestId"];common["openReceiptSha256"]=common["reopenReceiptSha256"];common.Remove("reopenRequestId");common.Remove("reopenReceiptSha256");OwnedOpenVerification.ValidateParameters(common);
  }
  internal static Dictionary<string,object> Verify(string root,string id,Dictionary<string,object> args,DocumentBindings.IContext context) {
   var result=Failure();try {
    Validate(args);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid saved reopen verifier identity.");string operation=(string)args["reopenRequestId"];
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var original=proofs.Json(Path.Combine(root,"responses",operation+".json"),(string)args["reopenReceiptSha256"],16777216);
     if(!Equals(original["operationId"],operation)||!new[]{"owned_saved_reopen_executed_unverified","owned_saved_reopen_outcome_unknown"}.Contains((string)original["state"])||!Equals(original["contextChangeAttempted"],true)||!Equals(original["cadSaveAttempted"],false)||!Equals(original["geometryMutationAttempted"],false))throw new IOException("Original attempted saved reopen receipt required.");
     var plan=proofs.Json(Path.Combine(root,"copyopenplans",operation+".json"),(string)original["planSha256"]);
     string[] fields={"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","modelId","windowNumber","expiresUtc","schemaVersion","operationId","operation","bridgeVersion","createdUtc","geometryExecutionEnabled","workingPath","reopenId","reopenSha256"};
     if(plan.Count!=fields.Length||fields.Any(k=>!plan.ContainsKey(k))||!Equals(plan["schemaVersion"],1)||!Equals(plan["operationId"],operation)||!Equals(plan["operation"],"open_owned_saved_result")||!Equals(plan["geometryExecutionEnabled"],false)||EntitySets.Canonical(plan)!=EntitySets.Canonical(original["plan"]))throw new IOException("Saved reopen execution plan differs.");
     foreach(string field in new[]{"copyId","ownershipSha256","workingPath","reopenId","reopenSha256"})if(!Equals(original[field],plan[field]))throw new IOException("Saved reopen response dependency differs.");
     using(var intent=new OwnedSavedReopenLease(root,OwnedSavedReopenExecution.Handle(plan),true)) {
      foreach(string field in new[]{"copyId","ownershipSha256","workingPath"})if(!Equals(plan[field],intent.Plan[field]))throw new IOException("Saved reopen ownership/provenance differs.");
      if(!Equals(original["sourceSavedResultSha256"],intent.Source.Saved["sha256"]))throw new IOException("Saved reopen source hash differs.");
      var claim=proofs.Json(Path.Combine(root,"workingcopies",(string)plan["copyId"],"open-once.json"),null);
      if(claim.Count!=5||!Equals(claim["operationId"],operation)||!Equals(claim["planSha256"],original["planSha256"])||!Equals(claim["copyId"],plan["copyId"])||!Equals(claim["ownershipSha256"],plan["ownershipSha256"])||!Equals(claim["automaticReplayForbidden"],true))throw new IOException("Saved reopen once claim differs.");
      var journal=NativeJournal.ForVerification(root,operation,(string)original["planSha256"]);
      if(journal.CommandReturnedDetails==null||journal.CommandReturnedDetails.Count!=1||!Equals(journal.CommandReturnedDetails["nativeRequestId"],operation))throw new IOException("Authoritative OPEN return missing; no replay.");
      // Pin all pre-existing journal files. Terminal append creates a NEW file.
      foreach(string path in Directory.GetFiles(Path.Combine(root,"operations",operation),"*.json"))proofs.Json(path,null);
      var expected=proofs.Json(Path.Combine(root,"responses",(string)args["inspectionRequestId"]+".json"),(string)args["inspectionSha256"],16777216);
      var listing=proofs.Json(Path.Combine(root,"responses",(string)args["listingRequestId"]+".json"),(string)args["listingSha256"],16777216);
      DateTime expiry=EntitySets.Expiry(args["expiresUtc"]),issued=EntitySets.Expiry(listing["timestampUtc"]);
      EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
      if(EntitySets.Expiry(expected["timestampUtc"])<=journal.CommandReturnedUtc||issued<=journal.CommandReturnedUtc||issued>DateTime.UtcNow.AddSeconds(5)||expiry>issued.AddSeconds(300)||expiry<issued||!Equals(listing["state"],"documents_listed_verified"))throw new InvalidOperationException("Separate fresh saved reopen reads required.");
      var active=listing["activeWindow"] as Dictionary<string,object>;
      if(active==null||!Equals(active["modelId"],args["modelId"])||!Equals(active["windowNumber"],args["windowNumber"]))throw new InvalidOperationException("Saved reopen verification choice is not active.");
      if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Saved reopen verification context changed.");
      DocumentBindings.Check(intent.Copy,expected,expiry,context,result,DocumentBindings.WindowDigest(listing));
      SavedReopenObservation.Check(intent.Source.Snapshot,expected);SavedReopenObservation.Check(intent.Source.Snapshot,context.Observation());
      var after=context.Windows();var target=WorkingCopyOpen.CheckAfter((Dictionary<string,object>)original["before"],after,intent.Copy.WorkingPath);
      var beforeHashes=(Dictionary<string,object>)original["savedFileHashesBefore"];var current=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)original["before"]);
      if(current.Count!=beforeHashes.Count||current.Any(p=>!beforeHashes.ContainsKey(p.Key)||!Equals(beforeHashes[p.Key],p.Value)))throw new IOException("Original saved files changed during saved reopen.");
      intent.Recheck();OwnedSaveTargets.NoFence(root,(string)plan["copyId"]);context.ConfirmDocument();
      if(DateTime.UtcNow>=expiry||DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(after))throw new InvalidOperationException("Saved reopen final verification context/expiry changed.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",operation},{"sha256",args["reopenReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"ownedModelId",target["modelId"]},{"sourceSavedResultSha256",intent.Source.Saved["sha256"]},{"verificationScope",SavedReopenObservation.Scope}});
      else if(!Equals(journal.LastDetails["noCommandReplay"],true)||!Equals(journal.LastDetails["sourceSavedResultSha256"],intent.Source.Saved["sha256"])||!Equals(journal.LastDetails["ownedModelId"],target["modelId"]))throw new IOException("Original saved reopen terminal proof differs.");
      // This independent verifier issues a NEW immutable passive binding from
      // the fresh receipt and retained current document; old IDs are not used.
      var bindingArgs=new Dictionary<string,object>{{"copyId",plan["copyId"]},{"ownershipSha256",plan["ownershipSha256"]},{"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},{"expiresUtc",args["expiresUtc"]}};
      DocumentBindings.Prepare(root,id,bindingArgs,expected,context,result);intent.Recheck();
      result["state"]="owned_saved_reopen_verified_observed";result["reopenOperationId"]=operation;result["reopenReceiptSha256"]=args["reopenReceiptSha256"];
      foreach(string field in new[]{"reopenId","reopenSha256","copyId","ownershipSha256","planSha256"})result[field]=original[field];
      result["sourceSavedResultSha256"]=intent.Source.Saved["sha256"];result["recordedJournalState"]=journal.State;result["expiresUtc"]=args["expiresUtc"];result["evidence"]=evidence;
      result["model"]=expected["model"];result["openedWindow"]=target;result["savedFilesUnchanged"]=true;result["noCommandReplay"]=true;result["observableContentChecked"]=true;
      result["comparisonScope"]=SavedReopenObservation.Scope;result["qualification"]=OwnedSavedReopenExecution.Qualification;
     }
    }
   }catch(Exception error){result["state"]="owned_saved_reopen_verification_rejected";result["error"]=error.Message;result["verificationMayBePartial"]=true;result["qualification"]="Inspect original OPEN journal and current receipts. No OPEN replay, automatic Save, close or proof repair. Binding artifact may exist; rejected call issues no usable binding.";}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  static Dictionary<string,object> Failure(){return new Dictionary<string,object>{{"state","owned_saved_reopen_verification_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"executionEnabled",false},{"retryAllowed",false}};}
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args) {
   var result=Failure();result["powerShapeConnectionAttempted"]=false;
   try {
    var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedSavedReopenExperimental)throw new InvalidOperationException("Saved reopen verifier disabled.");Validate(args);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     result["powerShapeConnectionAttempted"]=true;using(var context=new DocumentBindings.ComContext(config)){result=Verify(Program.Root,id,args,context);result["powerShapeConnectionAttempted"]=true;}
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
