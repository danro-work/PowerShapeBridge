using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerShapeBridge {
 // Narrow saved-reopen profile, separately gated in the full queue/SDK. This closes
 // one explicitly disposable verified saved-reopen copy, with its saved source
 // still open. No general document-close or unsaved-user-document authority.
 internal static class OwnedSavedCopyClose {
  internal const string Command="FILE CLOSE SELECTED YES";
  internal static readonly string[] Recipes={"execute_owned_saved_copy_close","verify_owned_saved_copy_close"};
  internal interface IContext:IDisposable {Dictionary<string,object> Windows();Dictionary<string,object> Observation();void ConfirmDocument();void Close();}
  internal sealed class ComContext:IContext {
   object app,document;
   internal ComContext(Config config){try{app=DocumentSession.Attach(config);dynamic ps=app;document=ps.ActiveDocument;ConfirmDocument();}catch{Dispose();throw;}}
   public Dictionary<string,object> Windows(){return DocumentSession.ReadStable(app);}
   public Dictionary<string,object> Observation(){return Probe.ReadInContext(app,document);}
   public void ConfirmDocument(){dynamic ps=app;if(Convert.ToBoolean(ps.Busy())||!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("Active document unavailable/busy.");object current=null;IntPtr a=IntPtr.Zero,b=IntPtr.Zero;try{current=ps.ActiveDocument;a=Marshal.GetIUnknownForObject(document);b=Marshal.GetIUnknownForObject(current);if(a!=b)throw new InvalidOperationException("Retained active COM document changed.");}finally{if(a!=IntPtr.Zero)Marshal.Release(a);if(b!=IntPtr.Zero)Marshal.Release(b);if(current!=null&&Marshal.IsComObject(current))Marshal.ReleaseComObject(current);}}
   public void Close(){dynamic ps=app;ps.Exec(Command);}
   public void Dispose(){if(document!=null&&Marshal.IsComObject(document))Marshal.ReleaseComObject(document);document=null;if(app!=null&&Marshal.IsComObject(app))Marshal.ReleaseComObject(app);app=null;}
  }
  internal static void Validate(Dictionary<string,object> args){
   string[] ids={"copyId","reopenVerificationRequestId","inspectionRequestId","listingRequestId"},hashes={"ownershipSha256","reopenVerificationReceiptSha256","inspectionSha256","listingSha256"};
   if(args.Count!=10||ids.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],32))||hashes.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],64))||!args.ContainsKey("discardOwnedTestCopy")||!Equals(args["discardOwnedTestCopy"],true)||!args.ContainsKey("expiresUtc"))throw new ArgumentException("Explicit disposable saved-copy close parameters required.");EntitySets.Expiry(args["expiresUtc"]);
  }
  internal static void ValidateVerification(Dictionary<string,object> args){
   if(args.Count!=8||new[]{"copyId","closeRequestId","inspectionRequestId","listingRequestId"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],32))||new[]{"closeReceiptSha256","inspectionSha256","listingSha256"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],64))||!args.ContainsKey("expiresUtc"))throw new ArgumentException("Exact independent native close-verification parameters required.");EntitySets.Expiry(args["expiresUtc"]);
  }
  static string Key(Dictionary<string,object> w){var copy=new Dictionary<string,object>(w);copy.Remove("nativeWindowId");copy.Remove("selectedRaw");return EntitySets.Canonical(copy);}
  internal static bool SavedHashesEqual(object before,Dictionary<string,string> after){var expected=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(before));return expected.Count==after.Count&&after.All(p=>expected.ContainsKey(p.Key)&&Equals(expected[p.Key],p.Value));}
  internal static void CheckAfter(Dictionary<string,object> before,Dictionary<string,object> after,int target){
   foreach(string k in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(before[k],after[k]))throw new InvalidOperationException("Close process/session changed.");
   var old=WorkingCopyOpen.Windows(before);var rows=WorkingCopyOpen.Windows(after);var removed=old.Where(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],target)).ToArray();
   if(removed.Length!=1||(int)before["windowCount"]!=old.Length||(int)after["windowCount"]!=rows.Length||rows.Length!=old.Length-1||rows.Length<1||rows.Any(w=>Equals(w["modelId"],target))||rows.Select(w=>w["nativeWindowId"]).Distinct().Count()!=rows.Length||rows.Count(w=>Equals(w["selectedRaw"],1))!=1)throw new InvalidOperationException("Expected exactly the owned target window to disappear.");
   foreach(var w in old.Where(w=>!Equals(w["modelId"],target)))if(rows.Count(v=>Key(v)==Key(w))!=1)throw new InvalidOperationException("Another document/window changed during close.");
   var active=after["activeWindow"] as Dictionary<string,object>;if(active==null||!Equals(active["selectedRaw"],1)||rows.Count(w=>Key(w)==Key(active)&&Equals(w["selectedRaw"],1))!=1)throw new InvalidOperationException("Remaining active window not established.");
  }
  static Dictionary<string,object> Receipt(OwnedOperationPlans.Proofs p,string root,Dictionary<string,object> args,string id,string hash){return p.Json(Path.Combine(root,"responses",(string)args[id]+".json"),(string)args[hash],16777216);}
  static OwnedSavedReopenLease Source(OwnedOperationPlans.Proofs p,string root,Dictionary<string,object> args,out Dictionary<string,object> verified){
   verified=Receipt(p,root,args,"reopenVerificationRequestId","reopenVerificationReceiptSha256");
   if(!Equals(verified["state"],"owned_saved_reopen_verified_observed")||!Equals(verified["recordedJournalState"],"succeeded_verified")||!Equals(verified["copyId"],args["copyId"])||!Equals(verified["ownershipSha256"],args["ownershipSha256"]))throw new IOException("Verified saved-reopen ownership required.");
   var original=p.Json(Path.Combine(root,"responses",(string)verified["reopenOperationId"]+".json"),(string)verified["reopenReceiptSha256"],16777216);
   if(!Equals(original["state"],"owned_saved_reopen_executed_unverified")||!Equals(original["operationId"],verified["reopenOperationId"])||!Equals(original["planSha256"],verified["planSha256"])||!Equals(original["copyId"],args["copyId"])||!Equals(original["ownershipSha256"],args["ownershipSha256"])||!Equals(original["reopenId"],verified["reopenId"])||!Equals(original["reopenSha256"],verified["reopenSha256"]))throw new IOException("Original successful saved OPEN differs.");
   var journal=NativeJournal.ForVerification(root,(string)verified["reopenOperationId"],(string)verified["planSha256"]);if(journal.State!="succeeded_verified"||!Equals(journal.LastDetails["ownedModelId"],((Dictionary<string,object>)verified["model"])["id"]))throw new IOException("Saved OPEN terminal journal differs.");
   var claim=p.Json(Path.Combine(root,"workingcopies",(string)args["copyId"],"open-once.json"),null);if(!Equals(claim["operationId"],verified["reopenOperationId"])||!Equals(claim["planSha256"],verified["planSha256"])||!Equals(claim["automaticReplayForbidden"],true))throw new IOException("Saved OPEN claim differs.");
   return new OwnedSavedReopenLease(root,new Dictionary<string,object>{{"reopenId",verified["reopenId"]},{"reopenSha256",verified["reopenSha256"]}},true);
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,IContext context){
   var result=new Dictionary<string,object>{{"state","owned_saved_copy_close_rejected"},{"operationId",id},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"retryAllowed",false},{"nativeProviderQualified",context is ComContext},{"coreOnlyQualification",true}};NativeJournal journal=null;
   try{
    Validate(args);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid close request ID.");
    using(var proofs=new OwnedOperationPlans.Proofs()){
     Dictionary<string,object> verified;using(var intent=Source(proofs,root,args,out verified)){
      var expected=Receipt(proofs,root,args,"inspectionRequestId","inspectionSha256");var listing=Receipt(proofs,root,args,"listingRequestId","listingSha256");var model=(Dictionary<string,object>)expected["model"];var active=(Dictionary<string,object>)listing["activeWindow"];int target=(int)model["id"];DateTime expiry=EntitySets.Expiry(args["expiresUtc"]);
      if(!Equals(listing["state"],"documents_listed_verified")||expiry>EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300)||expiry>EntitySets.Expiry(expected["timestampUtc"]).AddSeconds(300)||EntitySets.Canonical(model)!=EntitySets.Canonical(verified["model"])||!Equals(active["modelId"],target)||!Equals(active["selectedRaw"],1))throw new InvalidOperationException("Fresh current saved-copy choice required.");
      var rows=WorkingCopyOpen.Windows(listing);var sourceModel=(Dictionary<string,object>)intent.Source.Snapshot["model"];
      if(rows.Length<2||rows.Length>32||(int)listing["windowCount"]!=rows.Length||rows.Any(w=>!w.ContainsKey("modelId"))||rows.Select(w=>w["modelId"]).Distinct().Count()!=rows.Length||rows.Select(w=>w["nativeWindowId"]).Distinct().Count()!=rows.Length||rows.Count(w=>Equals(w["modelId"],target))!=1||rows.Count(w=>Equals(w["modelId"],sourceModel["id"])&&Equals(w["modelPath"],sourceModel["path"]))!=1||Equals(target,sourceModel["id"]))throw new InvalidOperationException("Unique owned window plus retained saved-source window required.");
      if(!Equals(active["windowNumber"],rows.Max(w=>(int)w["windowNumber"]))||!Equals(model["name"],"Working_"+(string)args["copyId"]))throw new InvalidOperationException("Only the last, original-named saved-reopen test window is qualified.");
      intent.Copy.CheckPath((string)model["path"]);OwnedSaveTargets.NoFence(root,(string)args["copyId"]);SavedReopenObservation.Check(intent.Source.Snapshot,expected);
      if(OwnedCopies.Digest(intent.Copy.Work)!=(string)intent.Plan["workingSha256"])throw new IOException("Disposable working bytes differ from saved result.");
      string folder=Path.GetDirectoryName(intent.Copy.WorkingPath),once=Path.Combine(folder,"close-once.json"),fence=Path.Combine(folder,"mutation-fence.json");if(File.Exists(once))throw new IOException("Earlier close claim; replay forbidden.");
      Action check=()=>{intent.Recheck();OwnedSaveTargets.NoFence(root,(string)args["copyId"]);if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Close window context changed.");EntitySets.CheckObserved(expected,context.Observation(),new int[0][],expiry,DateTime.UtcNow);context.ConfirmDocument();};check();check();
      var saved=WorkingCopyOpen.SavedHashes(listing);Directory.CreateDirectory(Path.Combine(root,"ownedcloseplans"));var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"operationId",id},{"operation","close_disposable_saved_reopen_copy"},{"before",listing},{"savedFileHashesBefore",saved},{"command",Command},{"fullTopologyEquivalent",false}};if(System.Text.Encoding.UTF8.GetByteCount(Program.Json.Serialize(plan))>65536)throw new InvalidOperationException("Close plan size limit.");string sha=NativeJournal.Freeze(Path.Combine(root,"ownedcloseplans",id+".json"),plan);
      result["copyId"]=args["copyId"];result["planSha256"]=sha;result["before"]=listing;journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"copyId",args["copyId"]}});check();result["executionClaimSha256"]=NativeJournal.Freeze(once,new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"automaticReplayForbidden",true}});result["executionFenceSha256"]=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"reason","Owned saved-copy close awaiting independent verification"}});
      journal.Append("execution_claimed",new Dictionary<string,object>{{"copyId",args["copyId"]}});result["contextChangeAttempted"]=true;journal.Append("command_started",new Dictionary<string,object>{{"nativeRequestId",id}});context.Close();journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id}});
      var after=context.Windows();result["after"]=after;CheckAfter(listing,after,target);intent.Recheck();var hashes=WorkingCopyOpen.SavedHashes(listing);if(!SavedHashesEqual(saved,hashes))throw new IOException("Saved file changed during close.");journal.Append("executed_unverified",new Dictionary<string,object>{{"closedModelId",target},{"noAutomaticReplay",true}});
      result["state"]="owned_saved_copy_closed_unverified";result["copyId"]=args["copyId"];result["planSha256"]=sha;result["before"]=listing;result["after"]=after;result["savedFilesUnchanged"]=true;result["checkpointVerified"]=true;
     }
    }
   }catch(Exception error){result["error"]=error.Message;if(journal!=null&&Equals(result["contextChangeAttempted"],true)){result["state"]="owned_saved_copy_close_outcome_unknown";try{if(new[]{"execution_claimed","command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"error",error.Message}});}catch(Exception failed){result["journalError"]=failed.Message;}}}
   if(journal!=null)result["recordedJournalState"]=journal.State;result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Verify(string root,string id,Dictionary<string,object> args,IContext context){
   // Separate fresh observations, no close command, no activation or save.
   ValidateVerification(args);if(!(context is ComContext)||!EntitySets.Hex(id,32))throw new ArgumentException("Independent native close-verification context required.");
   using(var proofs=new OwnedOperationPlans.Proofs()){
    var original=Receipt(proofs,root,args,"closeRequestId","closeReceiptSha256");if(!new[]{"owned_saved_copy_closed_unverified","owned_saved_copy_close_outcome_unknown"}.Contains((string)original["state"])||!Equals(original["operationId"],args["closeRequestId"])||!Equals(original["nativeProviderQualified"],true)||!Equals(original["contextChangeAttempted"],true)||!Equals(original["cadSaveAttempted"],false)||!Equals(original["geometryMutationAttempted"],false))throw new IOException("Original attempted native close receipt required.");
    string operation=(string)args["closeRequestId"];var heldClaim=proofs.Json(Path.Combine(root,"workingcopies",(string)args["copyId"],"close-once.json"),(string)original["executionClaimSha256"]);string planSha=(string)heldClaim["planSha256"];if(!Equals(heldClaim["operationId"],operation)||original.ContainsKey("planSha256")&&!Equals(original["planSha256"],planSha))throw new IOException("Close claim/receipt plan differs.");
    var plan=proofs.Json(Path.Combine(root,"ownedcloseplans",operation+".json"),planSha);Validate(plan.Where(p=>!new[]{"schemaVersion","operationId","operation","before","savedFileHashesBefore","command","fullTopologyEquivalent"}.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value));if(!Equals(plan["operationId"],operation)||!Equals(plan["copyId"],args["copyId"])||!Equals(plan["schemaVersion"],1)||!Equals(plan["operation"],"close_disposable_saved_reopen_copy")||!Equals(plan["command"],Command))throw new IOException("Original close intent differs.");
    var journal=NativeJournal.ForVerification(root,operation,planSha);if(!new[]{"executed_unverified","outcome_unknown"}.Contains(journal.State)||!Equals(journal.CommandReturnedDetails["nativeRequestId"],operation))throw new IOException("Authoritative returned close journal required; no inferred return/replay.");
    var inspection=Receipt(proofs,root,args,"inspectionRequestId","inspectionSha256");var listing=Receipt(proofs,root,args,"listingRequestId","listingSha256");DateTime expiry=EntitySets.Expiry(args["expiresUtc"]);
    if(EntitySets.Expiry(inspection["timestampUtc"])<=journal.CommandReturnedUtc||EntitySets.Expiry(listing["timestampUtc"])<=journal.CommandReturnedUtc||expiry>EntitySets.Expiry(inspection["timestampUtc"]).AddSeconds(300)||expiry>EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300))throw new InvalidOperationException("Fresh post-close observations required.");
    Dictionary<string,object> verified;using(var intent=Source(proofs,root,plan,out verified)){
     CheckAfter((Dictionary<string,object>)plan["before"],listing,(int)((Dictionary<string,object>)verified["model"])["id"]);if(original.ContainsKey("after")&&DocumentBindings.WindowDigest(listing)!=DocumentBindings.WindowDigest((Dictionary<string,object>)original["after"]))throw new InvalidOperationException("Close window result changed.");
     for(int i=0;i<2;i++){if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Fresh close-verification windows changed.");EntitySets.CheckObserved(inspection,context.Observation(),new int[0][],expiry,DateTime.UtcNow);context.ConfirmDocument();}
     intent.Recheck();if(!SavedHashesEqual(plan["savedFileHashesBefore"],WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["before"])))throw new IOException("Saved file changed after close.");
     string folder=Path.GetDirectoryName(intent.Copy.WorkingPath),fence=Path.Combine(folder,"mutation-fence.json");var marker=NativeJournal.VerifiedJson(fence,(string)original["executionFenceSha256"],65536);var claim=proofs.Json(Path.Combine(folder,"close-once.json"),(string)original["executionClaimSha256"]);if(!Equals(marker["operationId"],operation)||!Equals(marker["planSha256"],planSha)||!Equals(claim["operationId"],operation)||!Equals(claim["planSha256"],planSha)||!Equals(claim["automaticReplayForbidden"],true)||DateTime.UtcNow>=expiry)throw new IOException("Matching close claim/fence/lifetime required.");
     var evidence=new[]{new Dictionary<string,object>{{"requestId",args["closeRequestId"]},{"sha256",args["closeReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"closedModelId",((Dictionary<string,object>)verified["model"])["id"]},{"noCommandReplay",true}});
     NativeJournal.VerifiedJson(fence,(string)original["executionFenceSha256"],65536);File.Delete(fence);return new Dictionary<string,object>{{"state","owned_saved_copy_close_verified"},{"verificationId",id},{"operationId",operation},{"planSha256",planSha},{"copyId",plan["copyId"]},{"recordedJournalState",journal.State},{"savedFilesUnchanged",true},{"noCommandReplay",true},{"nativeProviderQualified",true},{"coreOnlyQualification",true},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"timestampUtc",DateTime.UtcNow.ToString("o")}};
    }
   }
  }
  internal static Dictionary<string,object> Run(string id,string recipe,Dictionary<string,object> args){
   bool verify=recipe==Recipes[1];bool connected=false;
   try{
    var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedSavedCopyCloseExperimental||!Recipes.Contains(recipe))throw new InvalidOperationException("Saved-copy close disabled.");if(verify)ValidateVerification(args);else Validate(args);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
     // Reject provenance/absent-return faults before attaching to PowerShape.
     using(var proofs=new OwnedOperationPlans.Proofs()){
      if(verify){var raw=Receipt(proofs,Program.Root,args,"closeRequestId","closeReceiptSha256");var claim=proofs.Json(Path.Combine(Program.Root,"workingcopies",(string)args["copyId"],"close-once.json"),(string)raw["executionClaimSha256"]);var j=NativeJournal.ForVerification(Program.Root,(string)args["closeRequestId"],(string)claim["planSha256"]);if(!new[]{"executed_unverified","outcome_unknown"}.Contains(j.State)||!Equals(j.CommandReturnedDetails["nativeRequestId"],args["closeRequestId"]))throw new IOException("Returned pending close required.");}
      else{Dictionary<string,object> original;using(var source=Source(proofs,Program.Root,args,out original)){source.Recheck();OwnedSaveTargets.NoFence(Program.Root,(string)args["copyId"]);}}
     }
     connected=true;using(var context=new ComContext(config)){var result=verify?Verify(Program.Root,id,args,context):Execute(Program.Root,id,args,context);result["coreOnlyQualification"]=false;result["queueQualifiedRoute"]=true;result["bridgeVersion"]=Program.ReleaseVersion;result["powerShapeConnectionAttempted"]=true;return result;}
    }
   }catch(Exception error){return new Dictionary<string,object>{{"state",verify?"owned_saved_copy_close_verification_rejected":"owned_saved_copy_close_rejected"},{"error",error.Message},{"operationId",id},{"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"powerShapeConnectionAttempted",connected},{"retryAllowed",false},{"coreOnlyQualification",false},{"queueQualifiedRoute",true},{"timestampUtc",DateTime.UtcNow.ToString("o")}};}
  }
 }
}
