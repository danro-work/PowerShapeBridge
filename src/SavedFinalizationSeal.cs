using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Frozen successful seed lineage, detached from the live writer after close.
 // Does not recertify the writer's original SaveAs fingerprint.
 internal sealed class SavedFinalizationSeal : IDisposable {
  internal readonly Dictionary<string,object> Plan;
  readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
  readonly OwnedCopies.OwnershipLease owned;
  readonly FileStream seed;
  readonly OwnedDirectoryLease directory;
  internal static void Need(bool yes,string why){if(!yes)throw new InvalidDataException(why);}
  internal static bool Same(object a,object b){return EntitySets.Canonical(a)==EntitySets.Canonical(b);}
  internal SavedFinalizationSeal(string root,Dictionary<string,object> args){
   try {
    string id=(string)args["seedPreparationRequestId"];Need(EntitySets.Hex(id,32)&&EntitySets.Hex(args["seedPreparationReceiptSha256"],64),"Fixed seed proof required.");
    var raw=proofs.Json(Path.Combine(root,"responses",id+".json"),(string)args["seedPreparationReceiptSha256"],16777216);
    Need(Equals(raw["state"],"owned_saved_reopen_prepared_files")&&Equals(raw["reopenId"],id),"Successful existing seed preparation required.");
    foreach(string flag in new[]{"executionEnabled","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","powerShapeConnectionAttempted"})Need(Equals(raw[flag],false),"Seed must be prepared without CAD commands.");
    directory=new OwnedDirectoryLease(Path.Combine(root,"savedreopens",id));
    Plan=proofs.Json(Path.Combine(root,"savedreopens",id,"reopen.json"),(string)raw["reopenSha256"]);
    Need(Same(Plan,raw["reopen"])&&Equals(Plan["reopenId"],id)&&Equals(Plan["copyId"],id)&&Equals(Plan["operation"],"reopen_owned_saved_result")&&Equals(Plan["executionEnabled"],false)&&Same(Plan["directoryIdentity"],directory.Identity),"Frozen seed plan differs.");
    var saved=(Dictionary<string,object>)Plan["savedResult"];var record=(Dictionary<string,object>)Plan["seed"];
    string path=Path.Combine(root,"savedreopens",id,"DiskReopen_"+id+".psmodel");Need(Equals(record["path"],path)&&Equals(record["sha256"],saved["sha256"])&&Equals(record["length"],saved["length"]),"Canonical sealed seed differs from original saved bytes.");
    string verification,verificationSha;
    if(Equals(Plan["schemaVersion"],2)){
     var observation=proofs.Json(Path.Combine(root,"responses",(string)Plan["observationRequestId"]+".json"),(string)Plan["observationReceiptSha256"],16777216);
     Need(Equals(observation["state"],"current_saved_result_observed")&&Equals(observation["nativeProviderQualified"],true),"Original native saved-source observation required.");
     var report=proofs.Json(Path.Combine(root,"savedresultobservations",(string)Plan["observationRequestId"]+".json"),(string)observation["observationSha256"],16777216);Need(Same(report,observation["observation"]),"Original source report differs.");verification=(string)report["originalVerificationRequestId"];verificationSha=(string)report["originalVerificationReceiptSha256"];
    }else {Need(Equals(Plan["schemaVersion"],1),"Supported frozen seed schema required.");verification=(string)Plan["verificationRequestId"];verificationSha=(string)Plan["verificationReceiptSha256"];}
    var verified=proofs.Json(Path.Combine(root,"responses",verification+".json"),verificationSha,16777216);
    Need(Equals(verified["state"],"owned_save_as_verified_observed")&&Equals(verified["recordedJournalState"],"succeeded_verified")&&Equals(verified["saveOperationId"],Plan["saveOperationId"])&&Same(verified["savedResult"],saved)&&Equals(verified["targetId"],Plan["targetId"])&&Equals(verified["targetSha256"],Plan["targetSha256"]),"Historical verified SaveAs lineage differs.");
    var journal=NativeJournal.ForVerification(root,(string)Plan["saveOperationId"],(string)verified["planSha256"]);Need(journal.State=="succeeded_verified"&&Equals(journal.LastDetails["savedResultSha256"],saved["sha256"])&&Equals(journal.LastDetails["noCommandReplay"],true),"Original SaveAs terminal journal required.");
    owned=OwnedCopies.Hold(root,id,(string)Plan["ownershipSha256"]);var ownership=(Dictionary<string,object>)owned.Inspection["ownership"];
    Need(Same(ownership["source"],record)&&Equals(owned.WorkingPath,Plan["workingPath"])&&Equals(owned.CheckpointPath,Plan["checkpointPath"])&&Equals(owned.Inspection["workingSha256"],saved["sha256"])&&Equals(owned.CheckpointSha,saved["sha256"]),"Sealed derivative/checkpoint ownership differs.");
    OwnedCopies.NoReparse(path);seed=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);Recheck();
    var sourceIdentity=(Dictionary<string,object>)saved["fileIdentity"];Need(!Same(sourceIdentity,record["fileIdentity"])&&!Same(sourceIdentity,((Dictionary<string,object>)ownership["working"])["fileIdentity"])&&!Same(sourceIdentity,((Dictionary<string,object>)ownership["checkpoint"])["fileIdentity"]),"Writer and sealed files must have distinct physical identities.");
   }catch{Dispose();throw;}
  }
  internal void Recheck(){var record=(Dictionary<string,object>)Plan["seed"];string path=(string)record["path"];directory.Recheck();owned.Recheck();OwnedCopies.NoReparse(path);Need((File.GetAttributes(path)&FileAttributes.ReadOnly)!=0&&seed.Length==Convert.ToInt64(record["length"])&&Same(OwnedCopies.FileIdentity(seed,path,true),record["fileIdentity"])&&OwnedCopies.Digest(seed)==(string)record["sha256"]&&OwnedCopies.Digest(seed)==(string)record["sha256"],"Immutable seed changed.");}
  internal static Dictionary<string,string> OtherHashes(Dictionary<string,object> listing,string writer){var hashes=WorkingCopyOpen.SavedHashes(listing);var keys=hashes.Keys.Where(k=>string.Equals(OwnedCopies.LocalPath(k),OwnedCopies.LocalPath(writer),StringComparison.OrdinalIgnoreCase)).ToArray();Need(keys.Length==1,"Exactly one explicitly owned writer exclusion required.");hashes.Remove(keys[0]);return hashes;}
  internal static Dictionary<string,object> WriterAfter(Dictionary<string,object> before){var after=OwnedSaveAs.SavedProof((string)before["path"]);Need(Same(before["fileIdentity"],after["fileIdentity"]),"Finalized writer was replaced; stop.");return after;}
  public void Dispose(){if(seed!=null)seed.Dispose();if(owned!=null)owned.Dispose();if(directory!=null)directory.Dispose();proofs.Dispose();}
 }
}
