using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Independent getter-only diagnostic; it cannot export/select/release a fence.
 internal static class OwnedDgkExportVerification {
  internal interface IContext : DocumentBindings.IContext {
   Dictionary<string,object> ExportSettings();int Composite(int type,int id);int[][] SelectedKeys();bool Busy();
  }
  static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
  static void Fields(Dictionary<string,object> value,params string[] fields){Require(value!=null&&value.Count==fields.Length&&fields.All(value.ContainsKey),"Exact DGK proof schema required.");}
  static Dictionary<string,object>[] Rows(object value,int limit){var list=value as IList;Require(list!=null&&list.Count<=limit,"Bounded explicit proof rows required.");return list.Cast<Dictionary<string,object>>().ToArray();}
  static string Artifact(string root,string folder,object id){Require(EntitySets.Hex(id,32),"Fixed DGK proof identity required.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;}
  static void Fresh(OwnedDgkTargets.TargetLease target,IContext context,Dictionary<string,object> expected,DateTime expiry,
   Dictionary<string,object>[] targets,Dictionary<string,object> settings,Dictionary<string,object> saved,Dictionary<string,object> diagnostics) {
   Require(!context.Busy(),"Independent DGK read context busy.");
   DocumentBindings.Check(target.Copy,expected,expiry,context,diagnostics,(string)target.Plan["windowContextSha256"]);
   var keys=context.SelectedKeys();Require(keys!=null&&keys.Length==0,"Independent native selection must be empty.");
   foreach(var item in targets){var key=EntitySets.Keys(new[]{item["entityKey"]}).Single();Require(context.Composite(key[0],key[1])==(int)item["compositeId"],"Independent DGK item mapping differs.");}
   var observed=context.ExportSettings();OwnedDgkExport.Settings(observed,(Dictionary<string,object>)target.Plan["parameters"]);
   Require(EntitySets.Canonical(observed)==EntitySets.Canonical(settings),"Independent effective DGK settings differ.");
   var current=WorkingCopyOpen.SavedHashes(context.Windows());Require(saved.Count==current.Count&&current.All(pair=>saved.ContainsKey(pair.Key)&&Equals(saved[pair.Key],pair.Value)),"Independent saved documents changed.");
   target.RecheckDependencies();context.ConfirmDocument();Require(!context.Busy(),"Independent DGK context became busy.");
  }
  internal static Dictionary<string,object> Verify(string root,string id,Dictionary<string,object> input,IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_dgk_export_verification_rejected"},{"verificationId",id},
    {"exportAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},
    {"geometryVerified",false},{"nativeProviderQualified",false},{"copyFenceReleased",false},{"retryAllowed",false}};
   try {
    OwnedGeometryExportVerification.Validate(input);Require(EntitySets.Hex(id,32),"Independent DGK verification identity required.");
    var args=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(input));
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     string executionId=(string)args["executionRequestId"];
     var receipt=proofs.Json(Artifact(root,"responses",executionId),(string)args["executionReceiptSha256"],16000000);
     Require(Equals(receipt["operationId"],executionId)&&Equals(receipt["state"],"owned_dgk_export_executed_unverified")&&
      Equals(receipt["exportAttempted"],true)&&Equals(receipt["contextChangeAttempted"],true)&&Equals(receipt["selectionRestoredObserved"],true)&&
      Equals(receipt["geometryVerified"],false)&&Equals(receipt["geometryMutationAttempted"],false)&&Equals(receipt["cadSaveAttempted"],false)&&
      Equals(receipt["nativeProviderQualified"],false)&&Equals(receipt["nativeReturnCodeQualified"],false)&&Equals(receipt["copyFenceReleased"],false)&&
      Equals(receipt["retryAllowed"],false)&&Equals(receipt["copyExportFenced"],true)&&Equals(receipt["recordedJournalState"],"executed_unverified"),"Complete unqualified DGK receipt required; unknown not resolved here.");
     var plan=proofs.Json(Artifact(root,"dgkexportexecutions",executionId),(string)receipt["planSha256"],16000000);
     Fields(plan,"targetId","targetSha256","schemaVersion","operationId","operation","copyId","ownershipSha256","parameters","artifactManifest",
      "bundleDirectory","targetPath","targets","settings","windowContextSha256","savedFileHashesBefore","createdUtc","qualification");
     Require(Equals(plan["schemaVersion"],1)&&Equals(plan["operationId"],executionId)&&Equals(plan["operation"],"export_owned_dgk")&&
      EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"]),"Frozen DGK execution plan differs.");
     string journalFolder=Path.Combine(root,"operations",executionId);OwnedCopies.NoReparse(journalFolder);
     foreach(string path in Directory.GetFiles(journalFolder,"*.json"))proofs.Json(path,null);
     var journal=NativeJournal.ForVerification(root,executionId,(string)receipt["planSha256"]);
     Require(journal.State=="executed_unverified"&&Equals(journal.CommandReturnedDetails["exportCallbackReturned"],true)&&
      Equals(journal.CommandReturnedDetails["nativeReturnCodeQualified"],false),"DGK journal missing aggregate returned evidence.");
     using(var target=new OwnedDgkTargets.TargetLease(root,(string)plan["targetId"],(string)plan["targetSha256"],true)) {
      foreach(string field in new[]{"copyId","ownershipSha256","bundleDirectory","targetPath","windowContextSha256"})Require(Equals(plan[field],target.Plan[field]),"DGK target dependency differs.");
      foreach(string field in new[]{"parameters","artifactManifest"})Require(EntitySets.Canonical(plan[field])==EntitySets.Canonical(target.Plan[field]),"DGK bundle policy differs.");
      Require(Equals(receipt["copyId"],plan["copyId"])&&EntitySets.Expiry(plan["createdUtc"])>=EntitySets.Expiry(target.Plan["preparedUtc"])&&
       EntitySets.Expiry(plan["createdUtc"])<=journal.CommandReturnedUtc,"DGK plan/copy chronology differs.");
      var targets=Rows(plan["targets"],128);Require(targets.Length==target.Source.Keys.Length&&targets.Length>0,"DGK target count differs.");
      var inventory=EntitySets.Inventory(target.Source.Expected);
      for(int index=0;index<targets.Length;index++) {
       var key=target.Source.Keys[index];var item=inventory.Single(value=>Equals(value["nativeTypeCode"],key[0])&&Equals(value["nativeId"],key[1]));
       Fields(targets[index],"entityKey","type","name","levelNumber","compositeId");
       Require(targets[index]["compositeId"] is int&&(int)targets[index]["compositeId"]>0&&EntitySets.Canonical(targets[index]["entityKey"])==EntitySets.Canonical(key)&&
        Equals(targets[index]["type"],item["type"])&&Equals(targets[index]["name"],item["name"])&&Equals(targets[index]["levelNumber"],item["levelNumber"]),"DGK selection target differs from original set.");
      }
      Require(targets.Select(item=>item["compositeId"]).Distinct().Count()==targets.Length,"DGK direct mapping duplicated.");
      var settings=(Dictionary<string,object>)plan["settings"];OwnedDgkExport.Settings(settings,(Dictionary<string,object>)target.Plan["parameters"]);
      string folder=OwnedDgkTargets.Folder(root,(string)plan["targetId"]);
      var claim=proofs.Json(Path.Combine(folder,"export-once.json"),null);Fields(claim,"operationId","planSha256","targetId","automaticReplayForbidden");
      Require(Equals(claim["operationId"],executionId)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["targetId"],plan["targetId"])&&Equals(claim["automaticReplayForbidden"],true),"DGK one-use claim differs.");
      var fence=proofs.Json(OwnedGeometryExportTargets.ExportFencePath(root,(string)plan["copyId"]),(string)receipt["exportFenceSha256"]);
      Fields(fence,"schemaVersion","operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden");
      Require(Equals(fence["schemaVersion"],1)&&Equals(fence["operationId"],executionId)&&Equals(fence["planSha256"],receipt["planSha256"])&&
       Equals(fence["copyId"],plan["copyId"])&&Equals(fence["ownershipSha256"],plan["ownershipSha256"])&&Equals(fence["automaticReplayForbidden"],true),"Original DGK fence differs.");
      var summary=proofs.Json(Path.Combine(folder,"export-summary.json"),(string)receipt["summarySha256"],16000000);
      Fields(summary,"operationId","planSha256","commands","bundle","geometryVerified");
      Require(Equals(summary["operationId"],executionId)&&Equals(summary["planSha256"],receipt["planSha256"])&&Equals(summary["geometryVerified"],false)&&
       Equals(journal.LastDetails["summarySha256"],receipt["summarySha256"])&&EntitySets.Canonical(summary["bundle"])==EntitySets.Canonical(receipt["bundle"]),"DGK summary/receipt/journal linkage differs.");
      var commands=Rows(summary["commands"],130);Require(commands.Length==targets.Length+2&&Equals(journal.CommandReturnedDetails["callbackCalls"],commands.Length)&&
       Equals(receipt["callbackCallsAttempted"],commands.Length),"Incomplete DGK callback sequence.");
      for(int index=0;index<commands.Length;index++) {
       string kind=index<targets.Length?"add_selection":index==targets.Length?"export_dgk":"clear_own_selection";
       var command=commands[index];Fields(command,"kind","index","startedSha256","returnedSha256");
       Require(Equals(command["index"],index)&&Equals(command["kind"],kind),"DGK callback order differs.");
       string prefix=Path.Combine(folder,index.ToString("D6"));var started=proofs.Json(prefix+".started.json",(string)command["startedSha256"]);
       var returned=proofs.Json(prefix+".returned.json",(string)command["returnedSha256"]);
       Fields(returned,"operationId","planSha256","index","kind","startedSha256","callbackReturned");
       if(index<targets.Length)Fields(started,"operationId","planSha256","index","kind","target");
       else if(index==targets.Length)Fields(started,"operationId","planSha256","index","kind","path");
       else Fields(started,"operationId","planSha256","index","kind");
       Require(Equals(started["operationId"],executionId)&&Equals(returned["operationId"],executionId)&&Equals(started["planSha256"],receipt["planSha256"])&&
        Equals(returned["planSha256"],receipt["planSha256"])&&Equals(started["index"],index)&&Equals(returned["index"],index)&&
        Equals(started["kind"],kind)&&Equals(returned["kind"],kind)&&Equals(returned["startedSha256"],command["startedSha256"])&&Equals(returned["callbackReturned"],true),"DGK per-step linkage differs.");
       if(index<targets.Length)Require(EntitySets.Canonical(started["target"])==EntitySets.Canonical(targets[index]),"DGK selected item evidence differs.");
       else if(index==targets.Length)Require(Equals(started["path"],plan["targetPath"]),"DGK export path evidence differs.");
      }
      var expected=proofs.Json(Artifact(root,"responses",args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);
      var listing=proofs.Json(Artifact(root,"responses",args["listingRequestId"]),(string)args["listingSha256"],16000000);var expiry=EntitySets.Expiry(args["expiresUtc"]);
      EntitySets.CheckObserved(expected,expected,target.Source.Keys,expiry,DateTime.UtcNow);
      Require(EntitySets.Expiry(expected["timestampUtc"])>journal.CommandReturnedUtc&&EntitySets.Expiry(listing["timestampUtc"])>journal.CommandReturnedUtc&&
       EntitySets.Expiry(listing["timestampUtc"])<=DateTime.UtcNow&&expiry<=EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300)&&
       Equals(listing["state"],"documents_listed_verified"),"Independent DGK reads must follow aggregate return with their own valid lifetime.");
      Require(EntitySets.ObservedDigest(expected)==EntitySets.ObservedDigest(target.Source.Expected)&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Fresh DGK source/window context differs.");
      var active=(Dictionary<string,object>)listing["activeWindow"];Require(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"]),"Independent active DGK choice differs.");
      var saved=(Dictionary<string,object>)plan["savedFileHashesBefore"];
      Fresh(target,context,expected,expiry,targets,settings,saved,result);
      using(var bundle=new DgkBundleFiles.Lease(root,(string)plan["bundleDirectory"],target.BundlePlan)) {
       var recorded=(Dictionary<string,object>)summary["bundle"];
       Require(EntitySets.Expiry(recorded["timestampUtc"])<=journal.CommandReturnedUtc,"Bundle observation follows aggregate return.");
       foreach(string field in new[]{"state","schemaVersion","directory","directoryIdentity","artifacts","absentOptionalArtifacts","totalBytes",
        "cadGeometryVerified","exportSettingsQualified","ownershipAuthorized","cadConnectionAttempted","companionExecutionAttempted","qualification"})
        Require(EntitySets.Canonical(recorded[field])==EntitySets.Canonical(bundle.Report[field]),"Independent DGK bundle bytes/identity/content differ: "+field);
       Fresh(target,context,expected,expiry,targets,settings,saved,result);bundle.Recheck();
       var diagnostic=new Dictionary<string,object>{{"schemaVersion",1},{"verificationId",id},{"executionRequestId",executionId},
        {"executionReceiptSha256",args["executionReceiptSha256"]},{"planSha256",receipt["planSha256"]},
        {"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},
        {"listingRequestId",args["listingRequestId"]},{"listingSha256",args["listingSha256"]},{"bundle",bundle.Report},
        {"state","observed_dgk_bundle_context_match_unqualified"},{"copyFenceReleased",false},{"geometryVerified",false},{"nativeProviderQualified",false}};
       string path=Artifact(root,"dgkexportverifications",id);Directory.CreateDirectory(Path.GetDirectoryName(path));
       result["diagnosticSha256"]=NativeJournal.Freeze(path,diagnostic);result["state"]=diagnostic["state"];result["fileAndContextObservedMatch"]=true;
       result["recordedJournalState"]=journal.State;
      }
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
