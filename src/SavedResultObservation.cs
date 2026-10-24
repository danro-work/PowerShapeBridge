using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Passive retained-document core with an explicitly gated queue wrapper.
 // Historical SaveAs proof remains historical; only the CURRENT retained saved
 // document is observed. Different unrelated windows do not rewrite that proof.
 internal static class SavedResultObservation {
  internal const string Recipe="restore_owned_saved_result";
  internal const string Scope="current_saved_result_same_process_document_observation_v1";
  static Dictionary<string,object> Clone(Dictionary<string,object> d){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(d));}
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  internal static void Validate(Dictionary<string,object> args){
   string[] ids={"verificationRequestId","inspectionRequestId","listingRequestId"},hashes={"verificationReceiptSha256","inspectionSha256","listingSha256"};
   Need(args!=null&&args.Count==9&&ids.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],32))&&hashes.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],64))&&args.ContainsKey("windowNumber")&&args["windowNumber"] is int&&(int)args["windowNumber"]>0&&args.ContainsKey("modelId")&&args["modelId"] is int&&(int)args["modelId"]>0&&args.ContainsKey("expiresUtc"),"Exact current saved-result proof references required.");
   EntitySets.Expiry(args["expiresUtc"]);
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args){
   try{
    var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.ownedSavedResultRestoreExperimental,"Current saved-result restoration disabled.");Validate(args);
    string root=Program.Root;Directory.CreateDirectory(Path.Combine(root,"operations"));
    using(var guard=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var source=new OwnedSavedSource(root,new Dictionary<string,object>{{"verificationRequestId",args["verificationRequestId"]},{"verificationReceiptSha256",args["verificationReceiptSha256"]}},true)){
     source.Recheck();OwnedSavedResultClose.NoPending(root,(string)source.Verified["targetId"]); // File/journal dependencies before any COM attachment.
     using(var context=new DocumentBindings.ComContext(config))return Observe(root,id,args,context);
    }
   }catch(Exception error){return new Dictionary<string,object>{{"state","current_saved_result_observation_rejected"},{"error",error.Message},{"executionAuthority",false},{"sdkLeaseIssued",false},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"retryAllowed",false},{"localArtifactsMayExist",true},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"bridgeVersion",Program.ReleaseVersion}};}
  }
  internal static Dictionary<string,object> Observe(string root,string id,Dictionary<string,object> args,DocumentBindings.IContext context){
   Validate(args);args=Clone(args);Need(EntitySets.Hex(id,32),"New observation identity required.");root=OwnedCopies.LocalPath(root.TrimEnd('\\','/'));
   string folder=Path.Combine(root,"savedresultobservations"),path=Path.Combine(folder,id+".json");OwnedCopies.NoReparse(path);Need(!File.Exists(path)&&!Directory.Exists(path),"Observation identity already exists; no overwrite.");
   var sourceArgs=new Dictionary<string,object>{{"verificationRequestId",args["verificationRequestId"]},{"verificationReceiptSha256",args["verificationReceiptSha256"]}};
   using(var source=new OwnedSavedSource(root,sourceArgs,true))using(var proofs=new OwnedOperationPlans.Proofs()){
    OwnedSavedResultClose.NoPending(root,(string)source.Verified["targetId"]);
    var expected=proofs.Json(Path.Combine(root,"responses",(string)args["inspectionRequestId"]+".json"),(string)args["inspectionSha256"],16777216);
    var listing=proofs.Json(Path.Combine(root,"responses",(string)args["listingRequestId"]+".json"),(string)args["listingSha256"],16777216);
    DateTime expiry=EntitySets.Expiry(args["expiresUtc"]),now=DateTime.UtcNow,issued=EntitySets.Expiry(listing["timestampUtc"]);
    EntitySets.CheckObserved(expected,expected,new int[0][],expiry,now);
    Need(Equals(listing["state"],"documents_listed_verified")&&issued<=now.AddSeconds(5)&&issued>=EntitySets.Expiry(source.Snapshot["timestampUtc"])&&EntitySets.Expiry(expected["timestampUtc"])>=EntitySets.Expiry(source.Snapshot["timestampUtc"])&&expiry>now&&expiry>=issued&&expiry<=issued.AddSeconds(300),"Fresh bounded current listing required.");
    foreach(string k in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})Need(EntitySets.Canonical(expected[k])==EntitySets.Canonical(source.Snapshot[k]),"Original saved process/document identity changed.");
    foreach(string k in new[]{"processId","processStartedUtc","windowsSessionId"})Need(EntitySets.Canonical(expected[k])==EntitySets.Canonical(listing[k]),"Current listing process differs.");
    var model=(Dictionary<string,object>)expected["model"];var active=listing["activeWindow"] as Dictionary<string,object>;
    Need(active!=null&&Equals(active["modelId"],args["modelId"])&&Equals(model["id"],args["modelId"])&&Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelPath"],model["path"])&&Equals(active["modelName"],model["name"]),"Explicit active saved-result choice differs.");
    Need(WorkingCopyOpen.Windows(listing).Count(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],model["id"]))==1,"Current saved document has ambiguous views.");
    SavedReopenObservation.Check(source.Snapshot,expected);source.Recheck();
    string windows=DocumentBindings.WindowDigest(listing);Need(DocumentBindings.WindowDigest(context.Windows())==windows,"Current windows changed before observation.");
    var savedBefore=WorkingCopyOpen.SavedHashes(listing);
    for(int pass=0;pass<2;pass++){
     context.ConfirmDocument();var observed=Clone(context.Observation());
     Need(EntitySets.ObservedDigest(observed)==EntitySets.ObservedDigest(expected),"Saved document changed during retained observation.");
     Need(DocumentBindings.WindowDigest(context.Windows())==windows,"Current windows changed during observation.");source.Recheck();
    }
    var savedAfter=WorkingCopyOpen.SavedHashes(listing);Need(savedBefore.Count==savedAfter.Count&&savedBefore.All(p=>savedAfter.ContainsKey(p.Key)&&savedAfter[p.Key]==p.Value),"Currently open saved bytes changed during observation.");
    source.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==windows,"Final current context/expiry changed.");
    bool qualified=context is DocumentBindings.ComContext;
    var report=new Dictionary<string,object>{{"schemaVersion",1},{"observationId",id},{"state",qualified?"current_saved_result_observed":"current_saved_result_observed_unqualified"},{"qualificationScope",Scope},{"parameters",Clone(args)},
     {"nativeProviderQualified",qualified},{"executionAuthority",false},{"sdkLeaseIssued",false},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"originalJournalModified",false},{"historicalWindowInventoryRecertified",false},{"fullTopologyEquivalent",false},
     {"originalVerificationRequestId",args["verificationRequestId"]},{"originalVerificationReceiptSha256",args["verificationReceiptSha256"]},{"saveOperationId",source.Verified["saveOperationId"]},{"targetId",source.Plan["targetId"]},{"targetSha256",source.Plan["targetSha256"]},{"savedResult",Clone(source.Saved)},
     {"copyId",source.Plan["copyId"]},{"ownershipSha256",source.Plan["ownershipSha256"]},{"model",Clone(model)},{"observedPropertiesSha256",EntitySets.ObservedDigest(expected)},{"currentWindowContextSha256",windows},{"currentSavedFileHashes",savedAfter},{"sameContextObservationReads",2},{"expiresUtc",args["expiresUtc"]},{"timestampUtc",DateTime.UtcNow.ToString("o")}};
    // Even synthetic observations are evidence only, never a trusted reconnect.
    Directory.CreateDirectory(folder);OwnedCopies.NoReparse(folder);string sha=NativeJournal.Freeze(path,report);
    return new Dictionary<string,object>{{"state",report["state"]},{"observationId",id},{"observationSha256",sha},{"observation",report},{"nativeProviderQualified",qualified},{"executionAuthority",false},{"sdkLeaseIssued",false}};
   }
  }
 }
}
