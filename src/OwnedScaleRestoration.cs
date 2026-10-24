using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // A new observation of an existing successfully scaled saved-reopen copy.
 // Never repairs a journal, repeats OPEN/scale, removes a fence or saves CAD.
 internal static class OwnedScaleRestoration {
  internal const string Recipe="restore_owned_solid_scale";
  internal const string Scope="current_owned_saved_reopen_after_verified_scale_v1";
  static void Need(bool yes,string why){if(!yes)throw new InvalidDataException(why);}
  static string PathFor(string root,string folder,object id){Need(EntitySets.Hex(id,32),"Fixed restoration proof ID required.");string p=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(p);return p;}
  internal static Dictionary<string,object> Common(Dictionary<string,object> args){
   Need(args.Count==11&&args.ContainsKey("scaleVerificationRequestId")&&EntitySets.Hex(args["scaleVerificationRequestId"],32)&&args.ContainsKey("scaleVerificationReceiptSha256")&&EntitySets.Hex(args["scaleVerificationReceiptSha256"],64),"Exact original successful scale-verifier reference required.");
   var common=new Dictionary<string,object>(args);common.Remove("scaleVerificationRequestId");common.Remove("scaleVerificationReceiptSha256");OwnedRotationVerification.Validate(common);return common;
  }
  internal static Dictionary<string,object> Original(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs){
   var original=proofs.Json(PathFor(root,"responses",args["scaleVerificationRequestId"]),(string)args["scaleVerificationReceiptSha256"],16000000);
   Need(Equals(original["state"],"owned_solid_scale_verified_observed")&&Equals(original["verificationId"],args["scaleVerificationRequestId"])&&Equals(original["executionRequestId"],args["executionRequestId"])&&Equals(original["qualificationScope"],OwnedSolidScale.Scope)&&Equals(original["recordedJournalState"],"succeeded_verified")&&Equals(original["samplePostconditionsPassed"],true)&&Equals(original["copyFenceReleased"],true),"Only an original successful qualified scale verifier can restore access.");
   foreach(string k in new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","retryAllowed","fullTopologyEquivalent"})Need(Equals(original[k],false),"Original verifier has incompatible side effects/scope.");
   var diagnostic=proofs.Json(PathFor(root,"solidscaleverifications",args["scaleVerificationRequestId"]),(string)original["diagnosticSha256"],16000000);
   Need(Equals(diagnostic["verificationId"],args["scaleVerificationRequestId"])&&Equals(diagnostic["executionRequestId"],args["executionRequestId"])&&Equals(diagnostic["executionReceiptSha256"],args["executionReceiptSha256"])&&Equals(diagnostic["qualificationScope"],OwnedSolidScale.Scope),"Original scale diagnostic provenance differs.");return original;
  }
  internal static OwnedSavedReopenLease History(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs,Dictionary<string,object> original,Dictionary<string,object> binding,Dictionary<string,object> packet,NativeJournal journal){
   Need(journal.State=="succeeded_verified"&&Equals(journal.LastDetails["diagnosticSha256"],original["diagnosticSha256"])&&Equals(journal.LastDetails["noCommandReplay"],true)&&Equals(journal.LastDetails["verificationScope"],OwnedSolidScale.Scope),"Original terminal scale journal differs.");
   foreach(string k in new[]{"copyId","ownershipSha256"})Need(Equals(original[k],packet[k]),"Scale restoration ownership differs.");
   var diagnostic=proofs.Json(PathFor(root,"solidscaleverifications",args["scaleVerificationRequestId"]),(string)original["diagnosticSha256"],16000000);
   var evidence=new[]{new Dictionary<string,object>{{"requestId",args["executionRequestId"]},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",diagnostic["inspectionRequestId"]},{"sha256",diagnostic["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",diagnostic["listingRequestId"]},{"sha256",diagnostic["listingSha256"]}}};
   Need(EntitySets.Canonical(journal.LastDetails["evidence"])==EntitySets.Canonical(evidence),"Original terminal scale evidence differs.");
   string openVerifier=(string)binding["bindingId"];string response=PathFor(root,"responses",openVerifier);var opened=proofs.Json(response,null,16777216);
   Need(Equals(opened["bindingId"],openVerifier)&&Equals(opened["bindingSha256"],packet["bindingSha256"])&&EntitySets.Canonical(opened["binding"])==EntitySets.Canonical(binding),"Original saved OPEN binding provenance differs.");
   var refs=new Dictionary<string,object>{{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"reopenVerificationRequestId",openVerifier},{"reopenVerificationReceiptSha256",Program.Hash(response)}};
   foreach(string path in Directory.GetFiles(Path.Combine(root,"operations",(string)args["executionRequestId"]),"*.json"))proofs.Json(path,null);
   Dictionary<string,object> verified;return OwnedSavedCopyClose.Source(proofs,root,refs,out verified);
  }
  internal static void Complete(string root,string id,Dictionary<string,object> args,Dictionary<string,object> packet,Dictionary<string,object> expected,DocumentBindings.IContext context,Dictionary<string,object> result){
   OwnedSaveTargets.NoFence(root,(string)packet["copyId"]);
   var bindingArgs=new Dictionary<string,object>{{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},{"expiresUtc",args["expiresUtc"]}};
   DocumentBindings.Prepare(root,id,bindingArgs,expected,context,result);
   var report=new Dictionary<string,object>{{"schemaVersion",1},{"restorationId",id},{"scope",Scope},{"parameters",args},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"bindingId",result["bindingId"]},{"bindingSha256",result["bindingSha256"]},{"diagnosticSha256",result["diagnosticSha256"]},{"model",expected["model"]},{"expiresUtc",args["expiresUtc"]},{"noCommandReplay",true},{"originalJournalModified",false},{"geometrySaved",false},{"fullTopologyEquivalent",false}};
   string path=PathFor(root,"solidscalerestorations",id);Directory.CreateDirectory(Path.GetDirectoryName(path));result["restorationSha256"]=NativeJournal.Freeze(path,report);result["restoration"]=report;
   result["state"]="owned_solid_scale_restored_observed";result["restorationId"]=id;result["restorationScope"]=Scope;result["copyId"]=packet["copyId"];result["ownershipSha256"]=packet["ownershipSha256"];result["model"]=expected["model"];result["expiresUtc"]=args["expiresUtc"];result["scaleVerificationRequestId"]=args["scaleVerificationRequestId"];result["scaleVerificationReceiptSha256"]=args["scaleVerificationReceiptSha256"];result["noCommandReplay"]=true;result["originalJournalModified"]=false;result["geometrySaved"]=false;result["recordedJournalState"]="succeeded_verified";
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args){
   var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.ownedSolidScaleRestoreExperimental,"Owned scaled-document restoration disabled.");Common(args);
   using(var proofs=new OwnedOperationPlans.Proofs()){Original(Program.Root,args,proofs);}
   using(var context=new DocumentBindings.ComContext(config)){return OwnedSolidScale.Verify(Program.Root,id,args,context,true);}
  }
 }
}
