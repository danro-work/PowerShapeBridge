using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Passive preparation and revalidation. No mutation callbacks or COM entry point.
 internal static class OwnedLevelMovePlans {
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  static void Fields(Dictionary<string,object> value,params string[] keys){Need(value!=null&&value.Count==keys.Length&&keys.All(value.ContainsKey),"Level move fields differ.");}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal static Dictionary<string,object> Parameters(Dictionary<string,object> value) {
   Fields(value,"targetLevelNumber","targetLevelName","targetActiveRaw","selectionAfterPolicy","mode");
   Need(value["targetLevelNumber"] is int&&(int)value["targetLevelNumber"]>=0&&(int)value["targetLevelNumber"]<=997,"Explicit level integer 0..997 required.");
   Need(value["targetLevelName"] is string&&!string.IsNullOrWhiteSpace((string)value["targetLevelName"])&&((string)value["targetLevelName"]).Length<=4096,"Existing named target required.");
   Need(value["targetActiveRaw"] is int&&new[]{0,1}.Contains((int)value["targetActiveRaw"]),"Explicit target flag zero/one required.");
   Need(Equals(value["selectionAfterPolicy"],"cleared_by_documented_api")&&Equals(value["mode"],"move_in_place"),"Documented selection clear and in-place transfer required.");
   return Clone(value);
  }
  internal static void ValidateArguments(Dictionary<string,object> value,bool prepare) {
   if(prepare){Fields(value,"entitySetId","entitySetSha256","bindingId","bindingSha256","levelInspectionRequestId","levelInspectionSha256","parameters");Parameters(value["parameters"] as Dictionary<string,object>);}
   else Fields(value,"ownedLevelMoveId","ownedLevelMoveSha256");
   foreach(string key in value.Keys.Where(key=>key!="parameters"))Need(EntitySets.Hex(value[key],key.EndsWith("Sha256",StringComparison.Ordinal)?64:32),"Invalid level proof ID/SHA.");
  }
  static string Artifact(string root,string folder,object id) {
   Need(EntitySets.Hex(id,32),"Invalid level artifact ID.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;
  }
  static void NoFence(string root,Dictionary<string,object> binding,OwnedCopies.OwnershipLease lease) {
   OwnedGeometryExportTargets.NoExportFence(root,(string)binding["copyId"]);
   string path=Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"mutation-fence.json");Need(!File.Exists(path)&&!Directory.Exists(path),"Unresolved level move fence.");
  }
  static void Receipt(Dictionary<string,object> levels,Dictionary<string,object> expected,DateTime expires,string id,string windows) {
   Need(Equals(levels["state"],"native_levels_read_unqualified")&&Equals(levels["bridgeRequestId"],id),"Native level receipt identity differs.");
   foreach(string key in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})Need(Equals(levels[key],false),"Level receipt acquired authority.");
   Need(!levels.ContainsKey("viewportCapture"),"Level receipt contains capture.");
   var before=levels["before"] as Dictionary<string,object>;var after=levels["after"] as Dictionary<string,object>;
   Need(before!=null&&after!=null&&!before.ContainsKey("viewportCapture")&&!after.ContainsKey("viewportCapture"),"Complete passive level observations required.");
   EntitySets.CheckObserved(expected,before,new int[0][],expires,DateTime.UtcNow);EntitySets.CheckObserved(expected,after,new int[0][],expires,DateTime.UtcNow);
   DateTime stamp=EntitySets.Expiry(levels["timestampUtc"]);Need(stamp>=EntitySets.Expiry(expected["timestampUtc"])&&stamp<=DateTime.UtcNow.AddSeconds(5)&&stamp>=DateTime.UtcNow.AddSeconds(-300),"Level receipt time differs.");
   Need(DocumentBindings.WindowDigest((Dictionary<string,object>)levels["windowsBefore"])==windows&&DocumentBindings.WindowDigest((Dictionary<string,object>)levels["windowsAfter"])==windows,"Level receipt window identity differs.");
   Need(Equals(levels["slotCount"],1000)&&((IList)levels["levelSlots"]).Count==1000&&EntitySets.Canonical(levels["filterOrder"])==EntitySets.Canonical(new[]{"used","named","on"}),"Incomplete native slot/filter receipt.");
  }
  static Dictionary<string,object> BaseResult(){return new Dictionary<string,object>{{"state","owned_level_move_preparation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"executionEnabled",false},{"nativeProviderQualified",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false}};}
  internal static Dictionary<string,object> Check(string root,string requestId,Dictionary<string,object> arguments,bool prepare,NativeLevelReading.IContext context) {
   return CheckCore(root,requestId,arguments,prepare,context,null);
  }
  // The executor callback runs only after successful revalidation, while all
  // proof, working-byte and executor leases are retained. No public entry point.
  internal static void WithValidated(string root,string requestId,Dictionary<string,object> handle,NativeLevelReading.IContext context,Action<Dictionary<string,object>,Dictionary<string,object>,Dictionary<string,object>,OwnedCopies.OwnershipLease,OwnedOperationPlans.Proofs> callback) {
   var checkedResult=CheckCore(root,requestId,handle,false,context,callback);
   if(!Equals(checkedResult["state"],"owned_level_move_validated_observed"))throw new InvalidDataException(checkedResult.ContainsKey("error")?(string)checkedResult["error"]:"Level execution guard rejected.");
  }
  static Dictionary<string,object> CheckCore(string root,string requestId,Dictionary<string,object> arguments,bool prepare,NativeLevelReading.IContext context,Action<Dictionary<string,object>,Dictionary<string,object>,Dictionary<string,object>,OwnedCopies.OwnershipLease,OwnedOperationPlans.Proofs> callback) {
   var result=BaseResult();
   try {
    Need(EntitySets.Hex(requestId,32),"Invalid request ID.");ValidateArguments(arguments,prepare);
    // Detach nested caller data before every external read callback.
    var args=Clone(arguments);
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     Dictionary<string,object> packet=null;string id=prepare?requestId:(string)args["ownedLevelMoveId"],sha=prepare?null:(string)args["ownedLevelMoveSha256"];
     if(prepare)Need(!File.Exists(Artifact(root,"ownedlevelmoves",id))&&!Directory.Exists(Artifact(root,"ownedlevelmoves",id)),"Level plan already exists; replay forbidden.");
     if(!prepare) {
      packet=proofs.Json(Artifact(root,"ownedlevelmoves",id),sha,16000000);
      Fields(packet,"schemaVersion","ownedLevelMoveId","operation","preparedUtc","expiresUtc","executionEnabled","nativeProviderQualified","arguments","copyId","ownershipSha256","workingSha256","entityKeys","sourceLevelNumber","nativeObservedPropertiesSha256","windowContextSha256","requiredBeforeExecution");
      Need(Equals(packet["schemaVersion"],1)&&Equals(packet["ownedLevelMoveId"],id)&&Equals(packet["operation"],"move_entity_to_level")&&Equals(packet["executionEnabled"],false)&&Equals(packet["nativeProviderQualified"],false),"Prepared level policy differs.");
      OwnedOperationPlans.Prepared(root,id,sha,proofs);args=Clone((Dictionary<string,object>)packet["arguments"]);ValidateArguments(args,true);
     }
     var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};
     var loaded=DocumentBindings.Load(root,bindingArgs);var binding=proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);
     Need(EntitySets.Canonical(loaded)==EntitySets.Canonical(binding),"Binding changed during load.");
     var expected=proofs.Json(Artifact(root,"responses",binding["inspectionRequestId"]),(string)binding["inspectionSha256"],16000000);
     var set=proofs.Json(Artifact(root,"entitysets",args["entitySetId"]),(string)args["entitySetSha256"]);EntitySets.ValidatePrepare(set);
     var source=proofs.Json(Artifact(root,"responses",set["inspectionRequestId"]),(string)set["inspectionSha256"],16000000);
     var keys=EntitySets.Keys(set["entityKeys"]);Need(keys.Length==1&&keys[0][0]==40,"First accepted level move needs exactly one SOLID.");
     DateTime expires=new[]{EntitySets.Expiry(set["expiresUtc"]),EntitySets.Expiry(binding["expiresUtc"])}.Min();
     EntitySets.CheckObserved(source,expected,keys,expires,DateTime.UtcNow);
     Need(EntitySets.ObservedDigest(expected)==(string)binding["observedPropertiesSha256"],"Bound observation digest differs.");
     var entity=EntitySets.Inventory(expected).Single(item=>Equals(item["nativeTypeCode"],keys[0][0])&&Equals(item["nativeId"],keys[0][1]));
     Need(entity.ContainsKey("levelNumber")&&entity["levelNumber"] is int,"Source level missing.");
     var parameters=Parameters((Dictionary<string,object>)args["parameters"]);int target=(int)parameters["targetLevelNumber"];
     Need((int)entity["levelNumber"]>=0&&(int)entity["levelNumber"]<=997&&(int)entity["levelNumber"]!=target,"Invalid or no-op source level.");
     var levels=proofs.Json(Artifact(root,"responses",args["levelInspectionRequestId"]),(string)args["levelInspectionSha256"],16000000);
     Receipt(levels,expected,expires,(string)args["levelInspectionRequestId"],(string)binding["windowContextSha256"]);
     using(var lease=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"]))
     using(var pin=new OwnedWorkingBytePin(lease.WorkingPath)) {
      lease.Recheck();NoFence(root,binding,lease);DocumentBindings.Check(lease,expected,expires,context,result,(string)binding["windowContextSha256"]);
      var fresh=NativeLevelReading.Read(expected,expires,context);Need(Equals(fresh["state"],"native_levels_read_unqualified"),"Fresh native level read rejected.");
      foreach(string key in new[]{"levelSlots","filterFlagsRaw","filterOrder"})Need(EntitySets.Canonical(levels[key])==EntitySets.Canonical(fresh[key]),"Level slots/filters changed since receipt.");
      var row=(Dictionary<string,object>)((IList)fresh["levelSlots"])[target];
      Need(Equals(row["number"],target)&&Equals(row["name"],parameters["targetLevelName"])&&Convert.ToDouble(row["activeRaw"])==(int)parameters["targetActiveRaw"],"Explicit target slot changed.");
      var computed=new Dictionary<string,object>{{"schemaVersion",1},{"ownedLevelMoveId",id},{"operation","move_entity_to_level"},{"preparedUtc",prepare?DateTime.UtcNow.ToString("o"):packet["preparedUtc"]},{"expiresUtc",expires.ToString("o")},{"executionEnabled",false},{"nativeProviderQualified",false},{"arguments",args},
       {"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"entityKeys",keys},{"sourceLevelNumber",entity["levelNumber"]},{"nativeObservedPropertiesSha256",EntitySets.ObservedDigest(expected)},{"windowContextSha256",binding["windowContextSha256"]},
       {"requiredBeforeExecution",new[]{"qualified_set_items_level_provider","fresh_owned_context_and_composite_mapping","once_claim_and_mutation_fence","independent_level_and_geometry_verifier","saved_result_reopen","recovery_fault_matrix"}}};
      Need(EntitySets.Expiry(computed["preparedUtc"])>=EntitySets.Expiry(expected["timestampUtc"])&&EntitySets.Expiry(computed["preparedUtc"])<=DateTime.UtcNow.AddSeconds(5),"Prepared time differs.");
      NoFence(root,binding,lease);lease.Recheck();
      if(prepare) {
       string path=Artifact(root,"ownedlevelmoves",id);Directory.CreateDirectory(Path.GetDirectoryName(path));result["ownedLevelMoveId"]=id;result["localArtifactsMayExist"]=true;sha=NativeJournal.Freeze(path,computed);result["ownedLevelMoveSha256"]=sha;
       new NativeJournal(root,id,sha).Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"contextChangeAttempted",false}});packet=proofs.Json(path,sha,16000000);
       OwnedOperationPlans.Prepared(root,id,sha,proofs);
      }else Need(EntitySets.Canonical(packet)==EntitySets.Canonical(computed),"Prepared level dependencies changed.");
      DocumentBindings.Check(lease,expected,expires,context,result,(string)binding["windowContextSha256"]);
      var final=NativeLevelReading.Read(expected,expires,context);Need(Equals(final["state"],"native_levels_read_unqualified"),"Final native level read rejected.");
      foreach(string key in new[]{"levelSlots","filterFlagsRaw","filterOrder"})Need(EntitySets.Canonical(fresh[key])==EntitySets.Canonical(final[key]),"Level context changed during publication.");
      NoFence(root,binding,lease);lease.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expires&&DocumentBindings.WindowDigest(context.Windows())==(string)binding["windowContextSha256"],"Final level lifetime/window changed.");
      if(callback!=null)callback(Clone(packet),Clone(expected),Clone(final),lease,proofs);
      result["state"]=prepare?"owned_level_move_prepared_observed":"owned_level_move_validated_observed";result["ownedLevelMoveId"]=id;result["ownedLevelMoveSha256"]=sha;result["ownedLevelMove"]=Clone(packet);result["copyId"]=binding["copyId"];result["expiresUtc"]=expires.ToString("o");
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
