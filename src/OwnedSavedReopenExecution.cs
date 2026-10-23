using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedSavedReopenExecution {
  internal const string Qualification="One guarded OPEN of a prepared owned saved-result Working copy; original windows and saved files retained. Immutable saved provenance checked immediately before and after OPEN. Returned observation is unverified; separate verifier/new binding required. Unknown is never replayed, saved or closed automatically.";
  internal static void Validate(Dictionary<string,object> args) {
   string[] fields={"reopenId","reopenSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","modelId","windowNumber","expiresUtc"};
   if(args.Count!=fields.Length||fields.Any(k=>!args.ContainsKey(k)))throw new ArgumentException("Invalid saved reopen execution parameters.");
   OwnedSavedReopenLease.Validate(Handle(args));var common=new Dictionary<string,object>(args);common.Remove("reopenId");common.Remove("reopenSha256");common["copyId"]=args["reopenId"];common["ownershipSha256"]=new string('a',64);WorkingCopyOpen.ValidateParameters(common);
  }
  internal static Dictionary<string,object> Handle(Dictionary<string,object> args){return new Dictionary<string,object>{{"reopenId",args["reopenId"]},{"reopenSha256",args["reopenSha256"]}};}
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,WorkingCopyOpen.IContext context,int modelWindowLimit=20) {
   var result=Failure(id);try {
    Validate(args);using(var intent=new OwnedSavedReopenLease(root,Handle(args)))using(var reads=new OwnedOperationPlans.Proofs()) {
     var expected=reads.Json(Path.Combine(root,"responses",(string)args["inspectionRequestId"]+".json"),(string)args["inspectionSha256"],16777216);
     var listing=reads.Json(Path.Combine(root,"responses",(string)args["listingRequestId"]+".json"),(string)args["listingSha256"],16777216);
     if(EntitySets.Expiry(args["expiresUtc"])>intent.Expires)throw new InvalidOperationException("Saved reopen exceeds prepared lifetime.");
     // Fresh choice is the saved source document; user edits after SaveAs cannot
     // silently be certified as the saved bytes copied into this new document.
     foreach(string field in new[]{"processId","processStartedUtc","windowsSessionId","model"})if(EntitySets.Canonical(expected[field])!=EntitySets.Canonical(intent.Source.Snapshot[field]))throw new InvalidOperationException("Saved source active document/process differs.");
     if(OwnedSaveAs.Content(expected)!=OwnedSaveAs.Content(intent.Source.Snapshot))throw new InvalidOperationException("Active saved result changed since independent SaveAs verification.");
     SavedReopenObservation.Check(intent.Source.Snapshot,intent.Source.Snapshot);
     var admission=SavedReopenWindowPolicy.Check(modelWindowLimit,listing,intent.Source.Current,intent.Source.Current?(string)intent.Source.Verified["currentWindowContextSha256"]:null);
     OwnedSaveTargets.NoFence(root,(string)intent.Plan["copyId"]);
     var common=new Dictionary<string,object>(args);common.Remove("reopenId");common.Remove("reopenSha256");common["copyId"]=intent.Plan["copyId"];common["ownershipSha256"]=intent.Plan["ownershipSha256"];
     Action recheck=()=>{intent.Recheck();OwnedSaveTargets.NoFence(root,(string)intent.Plan["copyId"]);};
     result=WorkingCopyOpen.Execute(root,id,common,listing,expected,context,Handle(args),recheck,fresh=>SavedReopenObservation.Check(intent.Source.Snapshot,fresh));
     foreach(var entry in admission)result[entry.Key]=entry.Value;
     string state=(string)result["state"];result["state"]=state=="working_copy_opened_observed"?"owned_saved_reopen_executed_unverified":state=="working_copy_open_outcome_unknown"?"owned_saved_reopen_outcome_unknown":"owned_saved_reopen_rejected_before_change";
     result["reopenId"]=args["reopenId"];result["reopenSha256"]=args["reopenSha256"];result["sourceSavedResultSha256"]=intent.Source.Saved["sha256"];
     result["observableContentChecked"]=state=="working_copy_opened_observed";result["comparisonScope"]=SavedReopenObservation.Scope;result["qualification"]=Qualification;
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  static Dictionary<string,object> Failure(string id){return new Dictionary<string,object>{{"state","owned_saved_reopen_rejected_before_change"},{"operationId",id},{"bridgeVersion",Program.ReleaseVersion},{"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"powerShapeConnectionAttempted",false},{"retryAllowed",false},{"qualification",Qualification}};}
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args) {
   var result=Failure(id);try {
    var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedSavedReopenExperimental)throw new InvalidOperationException("Saved reopen execution disabled.");Validate(args);
    // Validate immutable disk intent before any PowerShape connection.
    using(var preflight=new OwnedSavedReopenLease(Program.Root,Handle(args)))preflight.Recheck();
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     result["powerShapeConnectionAttempted"]=true;using(var context=new WorkingCopyOpen.ComContext(config)){result=Execute(Program.Root,id,args,context,config.savedReopenModelWindowLimit);result["powerShapeConnectionAttempted"]=true;}
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
