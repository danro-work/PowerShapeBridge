using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // A separate journal-appending verifier. No SaveAs, selection, activation or
 // geometry call; it uses independently issued fresh read receipts.
 internal static class OwnedSaveVerification {
  internal static void Validate(Dictionary<string,object> args) {
   string[] ids={"saveRequestId","inspectionRequestId","listingRequestId"},shas={"saveReceiptSha256","inspectionSha256","listingSha256"};
   if(args.Count!=9||ids.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],32))||shas.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],64))||
      !args.ContainsKey("windowNumber")||!(args["windowNumber"] is int)||(int)args["windowNumber"]<1||
      !args.ContainsKey("modelId")||!(args["modelId"] is int)||(int)args["modelId"]<0||!args.ContainsKey("expiresUtc"))throw new ArgumentException("Invalid independent SaveAs verification parameters.");
   EntitySets.Expiry(args["expiresUtc"]);
  }
  static string Artifact(string root,string folder,string id){if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid SaveAs artifact identity.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  internal static Dictionary<string,object> Verify(string root,Dictionary<string,object> args,DocumentBindings.IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_save_as_verification_rejected"},{"bridgeVersion",Program.ReleaseVersion},
    {"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"retryAllowed",false},{"qualification",OwnedSaveAs.Qualification}};
   OwnedOperationPlans.Proofs fenceProofs=null;
   try {
    Validate(args);string id=(string)args["saveRequestId"];
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["saveReceiptSha256"],16777216);
     if(!Equals(receipt["operationId"],id)||!new[]{"owned_save_as_executed_unverified","owned_save_as_outcome_unknown"}.Contains((string)receipt["state"])||
        !Equals(receipt["cadSaveAttempted"],true)||!Equals(receipt["geometryMutationAttempted"],false)||!Equals(receipt["contextChangeAttempted"],true)||!Equals(receipt["saveCommandReturned"],true))throw new InvalidOperationException("Returned SaveAs receipt required; interrupted command cannot be certified.");
     var plan=proofs.Json(Artifact(root,"saveexecutions",id),(string)receipt["planSha256"]);
     string[] fields={"targetId","targetSha256","schemaVersion","operationId","operation","executionEnabled","copyId","ownershipSha256","resultPath","createdUtc"};
     if(plan.Count!=fields.Length||fields.Any(k=>!plan.ContainsKey(k))||!Equals(plan["schemaVersion"],1)||!Equals(plan["operationId"],id)||!Equals(plan["operation"],"save_owned_document_as")||!Equals(plan["executionEnabled"],true)||EntitySets.Canonical(plan)!=EntitySets.Canonical(receipt["plan"]))throw new IOException("SaveAs execution plan differs.");
     using(var target=new OwnedSaveTargets.TargetLease(root,(string)plan["targetId"],(string)plan["targetSha256"],true)) {
      foreach(string key in new[]{"copyId","ownershipSha256","resultPath"})if(!Equals(plan[key],target.Plan[key])||!Equals(receipt[key],plan[key]))throw new IOException("SaveAs target dependency differs.");
      var binding=DocumentBindings.Load(root,new Dictionary<string,object>{{"bindingId",target.Plan["bindingId"]},{"bindingSha256",target.Plan["bindingSha256"]}});
      var before=(Dictionary<string,object>)receipt["beforeWindows"];
      if(DocumentBindings.WindowDigest(before)!=(string)binding["windowContextSha256"])throw new IOException("Original SaveAs window proof differs.");
      string claimPath=Path.Combine(OwnedSaveTargets.Folder(root,(string)plan["targetId"]),"save-once.json");var claim=proofs.Json(claimPath,null);
      if(claim.Count!=4||!Equals(claim["operationId"],id)||!Equals(claim["planSha256"],receipt["planSha256"])||!Equals(claim["targetId"],plan["targetId"])||!Equals(claim["automaticReplayForbidden"],true))throw new IOException("Original SaveAs claim differs.");
      var journal=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]);var returned=journal.CommandReturnedDetails;
      if(returned==null||returned.Count!=2||!Equals(returned["nativeRequestId"],id)||!Equals(returned["saveCommandReturned"],true))throw new InvalidOperationException("Authoritative journal must confirm SaveAs return.");
      var expected=proofs.Json(Artifact(root,"responses",(string)args["inspectionRequestId"]),(string)args["inspectionSha256"],16777216);
      var listing=proofs.Json(Artifact(root,"responses",(string)args["listingRequestId"]),(string)args["listingSha256"],16777216);
      DateTime expiry=EntitySets.Expiry(args["expiresUtc"]),issued=EntitySets.Expiry(listing["timestampUtc"]);
      EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
      if(!Equals(listing["state"],"documents_listed_verified")||issued>DateTime.UtcNow.AddSeconds(5)||expiry>issued.AddSeconds(300)||expiry<issued||issued<=journal.CommandReturnedUtc||EntitySets.Expiry(expected["timestampUtc"])<=journal.CommandReturnedUtc)throw new InvalidOperationException("Independent fresh reads after SaveAs return required.");
      var active=(Dictionary<string,object>)listing["activeWindow"];var model=(Dictionary<string,object>)expected["model"];string path=(string)plan["resultPath"];
      if(!Equals(active["windowNumber"],args["windowNumber"])||!Equals(active["modelId"],args["modelId"])||!Equals(model["id"],active["modelId"])||!Equals(model["name"],active["modelName"])||!string.Equals(OwnedCopies.LocalPath((string)model["path"]),path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Verification choice is not the saved result.");
      foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(expected[key],listing[key])||!Equals(expected[key],target.Expected[key]))throw new InvalidOperationException("SaveAs verification process identity changed.");
      OwnedSaveAs.CheckAfterWindows(before,listing,target.Copy.WorkingPath,path);
      if(OwnedSaveAs.Content(expected)!=OwnedSaveAs.Content(target.Expected))throw new InvalidOperationException("Saved observable content/units differs from before SaveAs.");
      string windowsSha=DocumentBindings.WindowDigest(listing),fence=OwnedTranslation.FencePath(root,(string)plan["copyId"]);bool fenced=File.Exists(fence);
      if(fenced) {
       fenceProofs=new OwnedOperationPlans.Proofs();var record=fenceProofs.Json(fence,(string)receipt["executionFenceSha256"]);
       if(record.Count!=6||!Equals(record["schemaVersion"],1)||!Equals(record["operationId"],id)||!Equals(record["planSha256"],receipt["planSha256"])||!Equals(record["copyId"],plan["copyId"])||!Equals(record["ownershipSha256"],plan["ownershipSha256"])||!Equals(record["automaticReplayForbidden"],true))throw new IOException("SaveAs fence belongs to another operation.");
      }else if(journal.State!="succeeded_verified")throw new IOException("Unverified SaveAs fence missing.");
      var file=OwnedSaveAs.SavedProof(path);
      if(EntitySets.Canonical(file)!=EntitySets.Canonical(receipt["savedResult"]))throw new IOException("Saved result bytes/identity changed after original receipt.");
      if(DocumentBindings.WindowDigest(context.Windows())!=windowsSha)throw new InvalidOperationException("Verification windows changed.");
      for(int pass=0;pass<2;pass++) {
       EntitySets.CheckObserved(expected,context.Observation(),new int[0][],expiry,DateTime.UtcNow);context.ConfirmDocument();
       if(DocumentBindings.WindowDigest(context.Windows())!=windowsSha||EntitySets.Canonical(OwnedSaveAs.SavedProof(path))!=EntitySets.Canonical(file))throw new InvalidOperationException("SaveAs verification context/saved bytes changed.");
      }
      var saved=WorkingCopyOpen.SavedHashes(before);var old=(Dictionary<string,object>)receipt["savedFileHashesBefore"];
      if(saved.Count!=old.Count||saved.Any(pair=>!old.ContainsKey(pair.Key)||!Equals(old[pair.Key],pair.Value)))throw new IOException("An original saved model changed.");
      target.RecheckDependencies();context.ConfirmDocument();
      if(DateTime.UtcNow>=expiry||DocumentBindings.WindowDigest(context.Windows())!=windowsSha||EntitySets.Canonical(OwnedSaveAs.SavedProof(path))!=EntitySets.Canonical(file))throw new InvalidOperationException("SaveAs verification final lease changed.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["saveReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"savedResultSha256",file["sha256"]},{"verificationScope","new owned saved bytes and observable content/units; not full topology"}});
      if(fenced){fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fence,(string)receipt["executionFenceSha256"],65536);File.Delete(fence);}
      result["state"]="owned_save_as_verified_observed";result["saveOperationId"]=id;result["saveReceiptSha256"]=args["saveReceiptSha256"];result["planSha256"]=receipt["planSha256"];
      result["targetId"]=plan["targetId"];result["targetSha256"]=plan["targetSha256"];result["copyId"]=plan["copyId"];result["ownershipSha256"]=plan["ownershipSha256"];
      result["savedResult"]=file;result["model"]=model;result["expiresUtc"]=args["expiresUtc"];result["recordedJournalState"]=journal.State;
      result["copyFenceReleased"]=true;result["savedFilesUnchanged"]=true;result["noCommandReplay"]=true;result["evidence"]=evidence;
     }
    }
   }catch(Exception error){result["error"]=error.Message;result["recoveryInstructions"]="Inspect original journal/receipts; no SaveAs replay or fence repair. Verification append/fence release may be partial.";}
   finally{if(fenceProofs!=null)fenceProofs.Dispose();}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
