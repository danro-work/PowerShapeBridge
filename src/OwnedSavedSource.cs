using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // File/proof lease of an independently verified saved result; no COM or command.
 internal sealed class OwnedSavedSource : IDisposable {
  internal readonly Dictionary<string,object> Verified,Plan,Snapshot,Saved;
  internal readonly OwnedSaveTargets.TargetLease Target;
  internal readonly FileStream File;
  internal readonly DateTime Expires;
  readonly string root,saveId;
  readonly bool historical;
  readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
  internal static void Validate(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("verificationRequestId")||!EntitySets.Hex(args["verificationRequestId"],32)||
      !args.ContainsKey("verificationReceiptSha256")||!EntitySets.Hex(args["verificationReceiptSha256"],64))throw new ArgumentException("Invalid verified saved-result proof identity.");
  }
  string Artifact(string folder,string id){if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid saved-result artifact identity.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  static Dictionary<string,object>[] Evidence(object value) {
   var list=value as IList;if(list==null||list.Count!=3)throw new IOException("Three saved-result evidence references required.");
   return list.Cast<object>().Select(item=>{
    var record=item as Dictionary<string,object>;if(record==null||record.Count!=2||!EntitySets.Hex(record["requestId"],32)||!EntitySets.Hex(record["sha256"],64))throw new IOException("Invalid saved-result evidence reference.");return record;
   }).ToArray();
  }
  internal OwnedSavedSource(string bridgeRoot,Dictionary<string,object> args,bool historicalProof=false) {
   historical=historicalProof;
   root=OwnedCopies.LocalPath(bridgeRoot.TrimEnd('\\','/'));
   try {
    Validate(args);string verification=(string)args["verificationRequestId"];
    Verified=proofs.Json(Artifact("responses",verification),(string)args["verificationReceiptSha256"],16777216);
    if(!Equals(Verified["state"],"owned_save_as_verified_observed")||!Equals(Verified["recordedJournalState"],"succeeded_verified")||
       !Equals(Verified["copyFenceReleased"],true)||!Equals(Verified["savedFilesUnchanged"],true)||!Equals(Verified["noCommandReplay"],true)||
       !Equals(Verified["cadSaveAttempted"],false)||!Equals(Verified["geometryMutationAttempted"],false)||!Equals(Verified["contextChangeAttempted"],false)||
       Verified.ContainsKey("bridgeRequestId")&&!Equals(Verified["bridgeRequestId"],verification))throw new IOException("Independent saved-result receipt required.");
    DateTime issued=EntitySets.Expiry(Verified["timestampUtc"]),expiry=EntitySets.Expiry(Verified["expiresUtc"]);
    Expires=expiry<issued.AddSeconds(300)?expiry:issued.AddSeconds(300);
    if(issued>DateTime.UtcNow.AddSeconds(5)||!historical&&Expires<=DateTime.UtcNow||expiry<issued)throw new InvalidOperationException("Saved-result verification expired.");
    saveId=(string)Verified["saveOperationId"];var original=proofs.Json(Artifact("responses",saveId),(string)Verified["saveReceiptSha256"],16777216);
    if(!Equals(original["operationId"],saveId)||!new[]{"owned_save_as_executed_unverified","owned_save_as_outcome_unknown"}.Contains((string)original["state"])||
       !Equals(original["cadSaveAttempted"],true)||!Equals(original["contextChangeAttempted"],true)||!Equals(original["geometryMutationAttempted"],false)||!Equals(original["saveCommandReturned"],true))throw new IOException("Returned original SaveAs proof required.");
    Plan=proofs.Json(Artifact("saveexecutions",saveId),(string)Verified["planSha256"]);
    string[] fields={"targetId","targetSha256","schemaVersion","operationId","operation","executionEnabled","copyId","ownershipSha256","resultPath","createdUtc"};
    if(Plan.Count!=fields.Length||fields.Any(k=>!Plan.ContainsKey(k))||!Equals(Plan["schemaVersion"],1)||!Equals(Plan["operationId"],saveId)||!Equals(Plan["operation"],"save_owned_document_as")||!Equals(Plan["executionEnabled"],true)||
       !Equals(original["planSha256"],Verified["planSha256"])||EntitySets.Canonical(Plan)!=EntitySets.Canonical(original["plan"]))throw new IOException("Original saved-result execution plan differs.");
    Target=new OwnedSaveTargets.TargetLease(root,(string)Plan["targetId"],(string)Plan["targetSha256"],true);
    foreach(string key in new[]{"targetId","targetSha256","copyId","ownershipSha256"})if(!Equals(Verified[key],Plan[key]))throw new IOException("Verified saved-result dependency differs.");
    foreach(string key in new[]{"copyId","ownershipSha256","resultPath"})if(!Equals(Plan[key],Target.Plan[key])||!Equals(Plan[key],original[key]))throw new IOException("Saved-result target differs.");
    string claimPath=Path.Combine(OwnedSaveTargets.Folder(root,(string)Plan["targetId"]),"save-once.json");var claim=proofs.Json(claimPath,null);
    if(claim.Count!=4||!Equals(claim["operationId"],saveId)||!Equals(claim["planSha256"],Verified["planSha256"])||!Equals(claim["targetId"],Plan["targetId"])||!Equals(claim["automaticReplayForbidden"],true))throw new IOException("Original saved-result claim differs.");
    for(int sequence=0;sequence<6;sequence++)proofs.Json(Path.Combine(root,"operations",saveId,sequence.ToString("D6")+".json"),null);
    var journal=NativeJournal.ForVerification(root,saveId,(string)Verified["planSha256"]);
    if(journal.State!="succeeded_verified"||journal.CommandReturnedDetails==null||journal.CommandReturnedDetails.Count!=2||
       !Equals(journal.CommandReturnedDetails["nativeRequestId"],saveId)||!Equals(journal.CommandReturnedDetails["saveCommandReturned"],true)||
       !Equals(journal.LastDetails["noCommandReplay"],true))throw new IOException("Authoritative verified SaveAs journal required.");
    var evidence=Evidence(Verified["evidence"]);var terminal=Evidence(journal.LastDetails["evidence"]);
    if(!Equals(evidence[0]["requestId"],saveId)||!Equals(evidence[0]["sha256"],Verified["saveReceiptSha256"])||EntitySets.Canonical(terminal[0])!=EntitySets.Canonical(evidence[0]))throw new IOException("Saved-result evidence identity differs.");
    foreach(var entry in terminal)proofs.Json(Artifact("responses",(string)entry["requestId"]),(string)entry["sha256"],16777216);
    Snapshot=proofs.Json(Artifact("responses",(string)evidence[1]["requestId"]),(string)evidence[1]["sha256"],16777216);
    var listing=proofs.Json(Artifact("responses",(string)evidence[2]["requestId"]),(string)evidence[2]["sha256"],16777216);
    if(EntitySets.Expiry(Snapshot["timestampUtc"])<=journal.CommandReturnedUtc||EntitySets.Expiry(listing["timestampUtc"])<=journal.CommandReturnedUtc||
       !Equals(listing["state"],"documents_listed_verified")||EntitySets.Canonical(Snapshot["model"])!=EntitySets.Canonical(Verified["model"])||OwnedSaveAs.Content(Snapshot)!=OwnedSaveAs.Content(Target.Expected))throw new IOException("Saved-result observation/evidence differs.");
    var model=(Dictionary<string,object>)Snapshot["model"];var active=(Dictionary<string,object>)listing["activeWindow"];
    if(!Equals(model["id"],active["modelId"])||!Equals(model["name"],active["modelName"])||!Equals(model["path"],active["modelPath"])||
       !string.Equals(OwnedCopies.LocalPath((string)model["path"]),(string)Plan["resultPath"],StringComparison.OrdinalIgnoreCase))throw new IOException("Saved-result model path differs.");
    Saved=Verified["savedResult"] as Dictionary<string,object>;
    if(Saved==null||Saved.Count!=4||!Saved.ContainsKey("path")||!Saved.ContainsKey("sha256")||!Saved.ContainsKey("length")||!Saved.ContainsKey("fileIdentity")||
       EntitySets.Canonical(Saved)!=EntitySets.Canonical(original["savedResult"])||!Equals(Saved["sha256"],journal.LastDetails["savedResultSha256"])||!Equals(Saved["path"],Plan["resultPath"]))throw new IOException("Saved-result file proof differs.");
    File=new FileStream((string)Plan["resultPath"],FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
    Recheck();
   }catch{Dispose();throw;}
  }
  internal void Recheck() {
   Target.RecheckDependencies();OwnedSaveTargets.NoFence(root,(string)Plan["copyId"]);
   if(!historical&&DateTime.UtcNow>=Expires)throw new InvalidOperationException("Saved-result verification expired during clone.");
   string path=(string)Plan["resultPath"];OwnedCopies.NoReparse(path);
   if(!(Saved["length"] is int||Saved["length"] is long)||Convert.ToInt64(Saved["length"])<1||Convert.ToInt64(Saved["length"])>1073741824||
      File.Length!=Convert.ToInt64(Saved["length"])||EntitySets.Canonical(OwnedCopies.FileIdentity(File,path,true))!=EntitySets.Canonical(Saved["fileIdentity"])||
      OwnedCopies.Digest(File)!=(string)Saved["sha256"]||OwnedCopies.Digest(File)!=(string)Saved["sha256"])throw new IOException("Saved-result bytes/physical identity changed.");
   if(NativeJournal.ForVerification(root,saveId,(string)Verified["planSha256"]).State!="succeeded_verified")throw new IOException("Saved-result journal changed.");
  }
  public void Dispose(){if(File!=null)File.Dispose();if(Target!=null)Target.Dispose();proofs.Dispose();}
 }
}
