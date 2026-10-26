using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 // A fresh current-context observation. The completed OPEN and its verifier stay immutable.
 internal static class OwnedOpenAccessRestore {
  internal const string Recipe="restore_verified_owned_open",Scope="current_owned_document_after_verified_open_v1";
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  static string Fixed(string root,string dir,object id){Need(EntitySets.Hex(id,32),"Fixed restore identity required.");string p=Path.Combine(root,dir,(string)id+".json");OwnedCopies.NoReparse(p);return p;}
  internal static void Validate(Dictionary<string,object> args){
   Need(args.Count==11&&args.ContainsKey("verificationRequestId")&&EntitySets.Hex(args["verificationRequestId"],32)&&args.ContainsKey("verificationReceiptSha256")&&EntitySets.Hex(args["verificationReceiptSha256"],64),"Original successful OPEN verifier reference required.");
   var basic=new Dictionary<string,object>(args);basic.Remove("verificationRequestId");basic.Remove("verificationReceiptSha256");OwnedOpenVerification.ValidateParameters(basic);
  }
  internal sealed class Origin {
   internal Dictionary<string,object> Verified,Receipt,Plan;internal Dictionary<string,string> JournalHashes;
   internal Origin(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs){
    Validate(args);Verified=proofs.Json(Fixed(root,"responses",args["verificationRequestId"]),(string)args["verificationReceiptSha256"],16000000);
    Need(Equals(Verified["state"],"owned_open_verified_observed")&&Equals(Verified["recordedJournalState"],"succeeded_verified")&&Equals(Verified["openOperationId"],args["openRequestId"])&&Equals(Verified["openReceiptSha256"],args["openReceiptSha256"]),"Original successful OPEN verifier differs.");
    // Native responses historically omit bridgeRequestId; IPC adds it in memory.
    // The fixed response path plus exact original SHA is always required above.
    object inline;Need(!Verified.TryGetValue("bridgeRequestId",out inline)||Equals(inline,args["verificationRequestId"]),"Inline verifier identity differs.");
    Need(EntitySets.Hex(Verified["copyId"],32)&&EntitySets.Hex(Verified["ownershipSha256"],64)&&EntitySets.Hex(Verified["planSha256"],64),"Original fixed ownership identity required.");
    foreach(string flag in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","retryAllowed"})Need(Equals(Verified[flag],false),"Original verifier side effects differ.");
    Receipt=proofs.Json(Fixed(root,"responses",args["openRequestId"]),(string)args["openReceiptSha256"],16000000);
    Need(Equals(Receipt["operationId"],args["openRequestId"])&&Equals(Receipt["state"],"working_copy_opened_observed")&&Equals(Receipt["contextChangeAttempted"],true)&&Equals(Receipt["geometryMutationAttempted"],false)&&Equals(Receipt["cadSaveAttempted"],false),"Only a returned successful owned OPEN is supported.");
    foreach(string key in new[]{"copyId","ownershipSha256","planSha256"})Need(Equals(Verified[key],Receipt[key]),"Original ownership/plan differs.");
    Plan=proofs.Json(Fixed(root,"copyopenplans",args["openRequestId"]),(string)Receipt["planSha256"],65536);
    Need(Plan.Count==16&&Equals(Plan["schemaVersion"],1)&&Equals(Plan["operation"],"open_working_copy")&&Equals(Plan["operationId"],args["openRequestId"])&&Equals(Plan["geometryExecutionEnabled"],false)&&EntitySets.Canonical(Plan)==EntitySets.Canonical(Receipt["plan"]),"Original frozen OPEN plan differs.");
    foreach(string key in new[]{"copyId","ownershipSha256"})Need(Equals(Plan[key],Receipt[key]),"Original plan ownership differs.");
    var claim=proofs.Json(Path.Combine(root,"workingcopies",(string)Receipt["copyId"],"open-once.json"),null);
    Need(claim.Count==5&&Equals(claim["operationId"],args["openRequestId"])&&Equals(claim["planSha256"],Receipt["planSha256"])&&Equals(claim["copyId"],Receipt["copyId"])&&Equals(claim["ownershipSha256"],Receipt["ownershipSha256"])&&Equals(claim["automaticReplayForbidden"],true),"Original OPEN claim differs.");
    string journalDir=Path.Combine(root,"operations",(string)args["openRequestId"]);OwnedCopies.NoReparse(journalDir);
    Need(!File.Exists(Path.Combine(journalDir,"writer.lock"))&&Directory.GetFiles(journalDir).Length==6,"Complete original six-event successful OPEN required.");
    JournalHashes=Directory.GetFiles(journalDir,"*.json").OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p,p=>Program.Hash(p));
    foreach(var p in JournalHashes)proofs.Json(p.Key,p.Value);
    var journal=NativeJournal.ForVerification(root,(string)args["openRequestId"],(string)Receipt["planSha256"]);
    Need(journal.State=="succeeded_verified"&&Equals(journal.LastDetails["noCommandReplay"],true)&&EntitySets.Canonical(journal.LastDetails["evidence"])==EntitySets.Canonical(Verified["evidence"]),"Original terminal OPEN evidence differs.");
    var evidence=(IList)Verified["evidence"];Need(evidence.Count==3,"Original verifier evidence limit.");
    foreach(Dictionary<string,object> e in evidence)proofs.Json(Fixed(root,"responses",e["requestId"]),(string)e["sha256"],16000000);
    var after=(Dictionary<string,object>)Verified["after"];var target=WorkingCopyOpen.CheckAfter((Dictionary<string,object>)Receipt["before"],after,(string)Plan["workingPath"]);
    Need(EntitySets.Canonical(target)==EntitySets.Canonical(Verified["openedWindow"]),"Historical OPEN postcondition differs.");
   }
  }
  static FileStream Source(OwnedCopies.OwnershipLease lease){var record=(Dictionary<string,object>)((Dictionary<string,object>)lease.Inspection["ownership"])["source"];string p=(string)record["path"];OwnedCopies.NoReparse(p);var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read);try{Need(f.Length==Convert.ToInt64(record["length"])&&OwnedCopies.Digest(f)==(string)record["sha256"]&&EntitySets.Canonical(OwnedCopies.FileIdentity(f,p,false))==EntitySets.Canonical(record["fileIdentity"]),"Original source identity/bytes changed.");return f;}catch{f.Dispose();throw;}}
  static Dictionary<string,object> Base(){return new Dictionary<string,object>{{"state","owned_open_access_restore_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"cadMutationAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false},{"originalJournalModified",false},{"noCommandReplay",true},{"fullTopologyEquivalent",false}};}
  internal static Dictionary<string,object> Observe(string root,string id,Dictionary<string,object> args,Func<DocumentBindings.IContext> factory){
   Validate(args);Need(EntitySets.Hex(id,32),"Fresh restore identity required.");var result=Base();Directory.CreateDirectory(Path.Combine(root,"operations"));
   using(var guard=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()){
    var original=new Origin(root,args,proofs);string copy=(string)original.Receipt["copyId"],owner=(string)original.Receipt["ownershipSha256"];OwnedSaveTargets.NoFence(root,copy);
    string report=Fixed(root,"openaccessrestorations",id);Need(!File.Exists(report)&&!Directory.Exists(report),"Restore already recorded; never replay.");
    var expected=proofs.Json(Fixed(root,"responses",args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Fixed(root,"responses",args["listingRequestId"]),(string)args["listingSha256"],16000000);var expiry=EntitySets.Expiry(args["expiresUtc"]);
    Need(Equals(listing["state"],"documents_listed_verified")&&EntitySets.Expiry(listing["timestampUtc"])>EntitySets.Expiry(original.Verified["timestampUtc"])&&EntitySets.Expiry(expected["timestampUtc"])>EntitySets.Expiry(original.Verified["timestampUtc"])&&EntitySets.Expiry(listing["timestampUtc"])<=DateTime.UtcNow.AddSeconds(5)&&expiry>EntitySets.Expiry(listing["timestampUtc"])&&expiry<=EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300),"Fresh current restore observations required.");
    var active=(Dictionary<string,object>)listing["activeWindow"];var historical=(Dictionary<string,object>)original.Verified["openedWindow"];
    Need(Equals(active["modelId"],args["modelId"])&&Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],historical["modelId"]),"Current choice differs from original owned document.");
    foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(Equals(listing[key],original.Verified["after"]is Dictionary<string,object>?((Dictionary<string,object>)original.Verified["after"])[key]:null)&&Equals(expected[key],listing[key]),"Original/current process identity differs.");
    foreach(string k in new[]{"selectionBefore","selectionAfter"})Need(expected[k]is IList&&((IList)expected[k]).Count==0,"Empty selection required.");foreach(string k in new[]{"contextBefore","contextAfter"})Need(EntitySets.Canonical(expected[k])==EntitySets.Canonical(new[]{0,0}),"WORLD required.");
    using(var lease=OwnedCopies.Hold(root,copy,owner))using(var working=new OwnedWorkingBytePin(lease.WorkingPath))using(var source=Source(lease))using(var context=factory()){
     Need(Equals(lease.Inspection["workingFileChanged"],false)&&string.Equals(OwnedCopies.LocalPath((string)original.Plan["workingPath"]),lease.WorkingPath,StringComparison.OrdinalIgnoreCase),"Only original clean saved working bytes supported.");
     var hashes=WorkingCopyOpen.SavedHashes(listing);DocumentBindings.Check(lease,expected,expiry,context,result,DocumentBindings.WindowDigest(listing));
     var bindingArgs=new Dictionary<string,object>{{"copyId",copy},{"ownershipSha256",owner},{"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},{"expiresUtc",args["expiresUtc"]}};
     DocumentBindings.Prepare(root,id,bindingArgs,expected,context,result);
     Need(DocumentBindings.WindowDigest(context.Windows())==DocumentBindings.WindowDigest(listing),"Current window set changed during restore.");var afterHashes=WorkingCopyOpen.SavedHashes(context.Windows());Need(hashes.Count==afterHashes.Count&&hashes.All(p=>afterHashes.ContainsKey(p.Key)&&p.Value==afterHashes[p.Key]),"Current saved files changed during restore.");lease.Recheck();context.ConfirmDocument();OwnedSaveTargets.NoFence(root,copy);Need(DateTime.UtcNow<expiry,"Restore expired before commit.");
     bool native=context is DocumentBindings.ComContext;result["state"]=native?"owned_open_access_restored_observed":"owned_open_access_observed_unqualified";result["timestampUtc"]=DateTime.UtcNow.ToString("o");
     var frozen=new Dictionary<string,object>(result){{"schemaVersion",1},{"restorationId",id},{"qualificationScope",Scope},{"parameters",args},{"copyId",copy},{"ownershipSha256",owner},{"expiresUtc",args["expiresUtc"]},{"nativeProviderQualified",native},{"currentSavedFileHashes",hashes},{"originalJournalHashes",original.JournalHashes.Select(p=>new Dictionary<string,object>{{"name",Path.GetFileName(p.Key)},{"sha256",p.Value}}).ToArray()},{"historicalWindowPostconditionReapplied",false},{"historicalSavedContentCertified",false}};
     Directory.CreateDirectory(Path.GetDirectoryName(report));string sha=NativeJournal.Freeze(report,frozen);result["restorationId"]=id;result["restorationSha256"]=sha;result["restoration"]=frozen;result["qualificationScope"]=Scope;result["copyId"]=copy;result["ownershipSha256"]=owner;result["expiresUtc"]=args["expiresUtc"];result["nativeProviderQualified"]=native;return result;
    }
   }
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args){var result=Base();try{var c=Program.ReadConfig();Need(c.IsGeneralCore&&c.ownedOpenAccessRestoreExperimental,"Owned OPEN current-access restore disabled.");return Observe(Program.Root,id,args,()=>new DocumentBindings.ComContext(c));}catch(Exception e){result["error"]=e.Message;result["localArtifactsMayExist"]=true;return result;}}
 }
}
