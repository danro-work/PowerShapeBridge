using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Verify a returned open from independently issued read receipts; never open again.
 public static class OwnedOpenVerification {
  public static void ValidateParameters(Dictionary<string,object> args) {
   if(args.Count!=9||new[]{"openRequestId","inspectionRequestId","listingRequestId"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],32))||new[]{"openReceiptSha256","inspectionSha256","listingSha256"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],64))||!args.ContainsKey("expiresUtc")||!args.ContainsKey("windowNumber")||!(args["windowNumber"] is int)||(int)args["windowNumber"]<1||!args.ContainsKey("modelId")||!(args["modelId"] is int)||(int)args["modelId"]<0)throw new ArgumentException("Invalid owned-open verification proof.");EntitySets.Expiry(args["expiresUtc"]);
  }
  public static Dictionary<string,object> Run(string requestId,Dictionary<string,object> args) {
   return Verify(Program.Root,args,null);
  }
  internal static Dictionary<string,object> Verify(string root,Dictionary<string,object> args,Func<DocumentBindings.IContext> contextFactory) {
   var result=new Dictionary<string,object>{{"state","owned_open_verification_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false}};
   try {
    var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.workingCopyOpenExperimental)throw new InvalidOperationException("Owned-open verification disabled.");ValidateParameters(args);
    string opened=(string)args["openRequestId"];var receipt=EntitySets.Receipt(root,opened,(string)args["openReceiptSha256"]);
    if(!Equals(receipt["operationId"],opened)||!new[]{"working_copy_open_outcome_unknown","working_copy_opened_observed"}.Contains((string)receipt["state"])||!Equals(receipt["contextChangeAttempted"],true)||!Equals(receipt["geometryMutationAttempted"],false)||!Equals(receipt["cadSaveAttempted"],false)||!EntitySets.Hex(receipt["copyId"],32)||!EntitySets.Hex(receipt["ownershipSha256"],64))throw new InvalidOperationException("Returned owned-open receipt required.");
    var plan=NativeJournal.VerifiedJson(Path.Combine(root,"copyopenplans",opened+".json"),(string)receipt["planSha256"],65536);
    if(!Equals(plan["operation"],"open_working_copy")||!Equals(plan["operationId"],opened)||!Equals(plan["copyId"],receipt["copyId"])||!Equals(plan["ownershipSha256"],receipt["ownershipSha256"])||!Equals(plan["geometryExecutionEnabled"],false)||EntitySets.Canonical(plan)!=EntitySets.Canonical(receipt["plan"]))throw new InvalidOperationException("Original open plan differs.");
    var expected=EntitySets.Receipt(root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]);var listing=EntitySets.Receipt(root,(string)args["listingRequestId"],(string)args["listingSha256"]);var expiry=EntitySets.Expiry(args["expiresUtc"]);EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
    if(!Equals(listing["state"],"documents_listed_verified")||EntitySets.Expiry(listing["timestampUtc"])>DateTime.UtcNow.AddSeconds(5)||expiry>EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300)||expiry<EntitySets.Expiry(listing["timestampUtc"]))throw new InvalidOperationException("Verification listing lease invalid.");
    var active=(Dictionary<string,object>)listing["activeWindow"];if(!Equals(active["modelId"],args["modelId"])||!Equals(active["windowNumber"],args["windowNumber"]))throw new InvalidOperationException("Explicit verification choice is not active.");
    using(var guard=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var lease=OwnedCopies.Hold(root,(string)receipt["copyId"],(string)receipt["ownershipSha256"])) {
     if(!string.Equals(lease.WorkingPath,(string)plan["workingPath"],StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Open plan target differs from ownership.");
     var claim=NativeJournal.VerifiedJson(Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"open-once.json"),Program.Hash(Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"open-once.json")),65536);
     if(!Equals(claim["operationId"],opened)||!Equals(claim["planSha256"],receipt["planSha256"])||!Equals(claim["copyId"],receipt["copyId"])||!Equals(claim["ownershipSha256"],receipt["ownershipSha256"])||!Equals(claim["automaticReplayForbidden"],true))throw new InvalidOperationException("Open claim differs.");
     var journal=NativeJournal.ForVerification(root,opened,(string)receipt["planSha256"]);
     if(EntitySets.Expiry(expected["timestampUtc"])<=journal.CommandReturnedUtc||EntitySets.Expiry(listing["timestampUtc"])<=journal.CommandReturnedUtc)throw new InvalidOperationException("Independent read receipts must follow command return.");
     using(var context=contextFactory==null?new DocumentBindings.ComContext(config):contextFactory()) {
      if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Verification window context changed.");
      DocumentBindings.Check(lease,expected,expiry,context,result,DocumentBindings.WindowDigest(listing));var after=context.Windows();var target=WorkingCopyOpen.CheckAfter((Dictionary<string,object>)receipt["before"],after,lease.WorkingPath);
      var saved=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)receipt["before"]);var oldSaved=(Dictionary<string,object>)receipt["savedFileHashesBefore"];if(saved.Count!=oldSaved.Count||saved.Any(x=>!oldSaved.ContainsKey(x.Key)||!Equals(oldSaved[x.Key],x.Value)))throw new IOException("An original saved file changed.");
      lease.Recheck();context.ConfirmDocument();if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(after)||DateTime.UtcNow>=expiry)throw new InvalidOperationException("Verification context/expiry changed at final check.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}},new Dictionary<string,object>{{"requestId",opened},{"sha256",args["openReceiptSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"verificationScope","owned saved copy opened in one new model window; existing document identities and saved bytes preserved; not full topology"},{"ownedModelId",target["modelId"]},{"noCommandReplay",true}});
      result["state"]="owned_open_verified_observed";result["copyId"]=receipt["copyId"];result["ownershipSha256"]=receipt["ownershipSha256"];result["openOperationId"]=opened;result["openReceiptSha256"]=args["openReceiptSha256"];result["planSha256"]=receipt["planSha256"];result["recordedJournalState"]=journal.State;result["expiresUtc"]=args["expiresUtc"];result["openedWindow"]=target;result["before"]=receipt["before"];result["after"]=after;result["savedFilesUnchanged"]=true;result["nativeWindowIdsRenumbered"]=WorkingCopyOpen.Windows((Dictionary<string,object>)receipt["before"]).Any(old=>WorkingCopyOpen.Windows(after).Any(current=>Equals(old["windowNumber"],current["windowNumber"])&&!Equals(old["nativeWindowId"],current["nativeWindowId"])));result["evidence"]=evidence;
     }
    }
   }catch(Exception error){result["error"]=error.Message;result["qualification"]="No CAD command or replay. Verification/journal append may be partial; inspect original journal and receipts.";}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
