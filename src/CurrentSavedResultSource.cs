using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Explicit current-observation provenance. This is not the old SaveAs verifier
 // and does not issue an SDK/CAD lease. No COM, commands, or journal writes.
 internal sealed class CurrentSavedResultSource : IDisposable {
  internal readonly Dictionary<string,object> Report,Snapshot;
  internal readonly OwnedSavedSource Original;
  internal readonly DateTime Expires;
  readonly bool historical;
  readonly Func<DateTime> utcNow;
  readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
  readonly List<OwnedDirectoryLease> directories=new List<OwnedDirectoryLease>();
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  static bool Same(object a,object b){return EntitySets.Canonical(a)==EntitySets.Canonical(b);}
  static Dictionary<string,object> Object(object value){var d=value as Dictionary<string,object>;Need(d!=null,"Saved observation object required.");return d;}
  internal static void Validate(Dictionary<string,object> args){
   Need(args!=null&&args.Count==2&&args.ContainsKey("observationRequestId")&&EntitySets.Hex(args["observationRequestId"],32)&&args.ContainsKey("observationReceiptSha256")&&EntitySets.Hex(args["observationReceiptSha256"],64),"Exact current saved-result response reference required.");
  }
  string Artifact(string root,string folder,string id){Need(EntitySets.Hex(id,32),"Invalid current saved-result artifact identity.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  internal CurrentSavedResultSource(string root,Dictionary<string,object> args,bool historicalProof=false,Func<DateTime> clock=null){
   historical=historicalProof;utcNow=clock??(()=>DateTime.UtcNow);
   try {
    Validate(args);root=OwnedCopies.LocalPath(root.TrimEnd('\\','/'));string id=(string)args["observationRequestId"];
    foreach(string folder in new[]{root,Path.Combine(root,"responses"),Path.Combine(root,"savedresultobservations")})directories.Add(new OwnedDirectoryLease(folder));
    var raw=proofs.Json(Artifact(root,"responses",id),(string)args["observationReceiptSha256"],16777216);
    Need(Equals(raw["state"],"current_saved_result_observed")&&Equals(raw["observationId"],id)&&EntitySets.Hex(raw["observationSha256"],64)&&(!raw.ContainsKey("bridgeRequestId")||Equals(raw["bridgeRequestId"],id)),"Qualified current saved-result response required.");
    foreach(string flag in new[]{"executionAuthority","sdkLeaseIssued"})Need(Equals(raw[flag],false),"Observation must not claim issued authority.");
    Need(Equals(raw["nativeProviderQualified"],true),"Native current observation required.");
    Report=proofs.Json(Artifact(root,"savedresultobservations",id),(string)raw["observationSha256"],16777216);
    string[] fields={"schemaVersion","observationId","state","qualificationScope","parameters","nativeProviderQualified","executionAuthority","sdkLeaseIssued","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","originalJournalModified","historicalWindowInventoryRecertified","fullTopologyEquivalent","originalVerificationRequestId","originalVerificationReceiptSha256","saveOperationId","targetId","targetSha256","savedResult","copyId","ownershipSha256","model","observedPropertiesSha256","currentWindowContextSha256","currentSavedFileHashes","sameContextObservationReads","expiresUtc","timestampUtc"};
    Need(Report.Count==fields.Length&&fields.All(Report.ContainsKey)&&Same(Report,raw["observation"])&&Equals(Report["schemaVersion"],1)&&Equals(Report["observationId"],id)&&Equals(Report["state"],raw["state"])&&Equals(Report["qualificationScope"],SavedResultObservation.Scope)&&Equals(Report["nativeProviderQualified"],true)&&Equals(Report["sameContextObservationReads"],2),"Frozen current saved-result report differs.");
    foreach(string flag in new[]{"executionAuthority","sdkLeaseIssued","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","originalJournalModified","historicalWindowInventoryRecertified","fullTopologyEquivalent"})Need(Equals(Report[flag],false),"Current observation claims unsupported authority.");
    var parameters=Object(Report["parameters"]);SavedResultObservation.Validate(parameters);
    Need(Equals(parameters["verificationRequestId"],Report["originalVerificationRequestId"])&&Equals(parameters["verificationReceiptSha256"],Report["originalVerificationReceiptSha256"])&&Equals(parameters["expiresUtc"],Report["expiresUtc"]),"Original saved-result reference differs.");
    // Keep this refusal explicit even if OwnedSavedSource later supports new
    // source variants. Observation-as-original/self-reference never recurses.
    var original=proofs.Json(Artifact(root,"responses",(string)parameters["verificationRequestId"]),(string)parameters["verificationReceiptSha256"],16777216);
    Need(Equals(original["state"],"owned_save_as_verified_observed"),"Original successful SaveAs verifier required; no observation chains.");
    Original=new OwnedSavedSource(root,new Dictionary<string,object>{{"verificationRequestId",parameters["verificationRequestId"]},{"verificationReceiptSha256",parameters["verificationReceiptSha256"]}},true);
    foreach(string key in new[]{"saveOperationId","targetId","targetSha256","savedResult","copyId","ownershipSha256"})Need(Same(Report[key],Original.Verified[key]),"Current saved-result provenance differs.");
    Snapshot=proofs.Json(Artifact(root,"responses",(string)parameters["inspectionRequestId"]),(string)parameters["inspectionSha256"],16777216);
    var listing=proofs.Json(Artifact(root,"responses",(string)parameters["listingRequestId"]),(string)parameters["listingSha256"],16777216);
    DateTime issued=EntitySets.Expiry(Report["timestampUtc"]),inspectionAt=EntitySets.Expiry(Snapshot["timestampUtc"]),listingAt=EntitySets.Expiry(listing["timestampUtc"]),originalAt=EntitySets.Expiry(Original.Snapshot["timestampUtc"]);
    Expires=EntitySets.Expiry(Report["expiresUtc"]);
    Need(issued<=utcNow().AddSeconds(5)&&issued<Expires&&inspectionAt>=originalAt&&listingAt>=originalAt&&inspectionAt<=issued&&listingAt<=issued&&Expires<=inspectionAt.AddSeconds(300)&&Expires<=listingAt.AddSeconds(300),"Current saved-result lifetime/chronology differs.");
    foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})Need(Same(Snapshot[key],Original.Snapshot[key]),"Original saved process/document differs.");
    foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(Same(Snapshot[key],listing[key]),"Current saved listing process differs.");
    var model=Object(Snapshot["model"]);var active=Object(listing["activeWindow"]);
    Need(Equals(listing["state"],"documents_listed_verified")&&Same(Report["model"],model)&&Equals(model["id"],parameters["modelId"])&&Equals(active["modelId"],model["id"])&&Equals(active["windowNumber"],parameters["windowNumber"])&&Equals(active["modelPath"],model["path"])&&Equals(active["modelName"],model["name"])&&WorkingCopyOpen.Windows(listing).Count(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],model["id"]))==1,"Current saved active choice differs.");
    Need(Equals(Report["observedPropertiesSha256"],EntitySets.ObservedDigest(Snapshot))&&Equals(Report["currentWindowContextSha256"],DocumentBindings.WindowDigest(listing)),"Current saved observation digest differs.");
    SavedReopenObservation.Check(Original.Snapshot,Snapshot);Recheck();
   }catch{Dispose();throw;}
  }
  internal void Recheck(){
   if(!historical&&utcNow()>=Expires)throw new InvalidOperationException("Current saved-result observation expired.");
   Original.Recheck();foreach(var directory in directories)directory.Recheck();
  }
  public void Dispose(){if(Original!=null)Original.Dispose();proofs.Dispose();foreach(var directory in directories)directory.Dispose();}
 }
}
