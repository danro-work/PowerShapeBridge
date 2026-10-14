using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Internal callback qualification only: no production COM adapter/MCP recipe.
 internal static class OwnedDgkExport {
  internal interface IContext : DocumentBindings.IContext {
   Dictionary<string,object> ExportSettings();
   int Composite(int type,int id);
   int[][] SelectedKeys();
   bool Busy();
   void AddSelection(int type,int id,string name);
   void ExportDgk(string fixedPrimaryPath,Dictionary<string,object> plannedParameters);
   void ClearOwnSelection();
  }
  static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static string KeyDigest(int[][] keys){Require(keys!=null&&keys.Length<=128&&keys.All(k=>k!=null&&k.Length==2),"Bounded explicit selected keys required.");return EntitySets.Canonical(keys.OrderBy(k=>k[0]).ThenBy(k=>k[1]).ToArray());}
  static string SelectionDigest(object value) {
   var list=value as IList;Require(list!=null&&list.Count<=128,"Bounded observed selection required.");
   return EntitySets.Canonical(list.Cast<object>().Select(EntitySets.Canonical).OrderBy(item=>item,StringComparer.Ordinal).ToArray());
  }
  static string Type(int code){return code==40?"SOLID":code==21?"SURFACE":"COMPCURVE";}
  static void Settings(Dictionary<string,object> value,Dictionary<string,object> parameters) {
   string[] fields={"format","coordinateFrame","exportVariant","triangleCompanions","featureCompanions","effectiveOptionsSha256","providerQualification"};
   Require(value!=null&&value.Count==fields.Length&&fields.All(value.ContainsKey)&&Equals(value["format"],"dgk")&&
    EntitySets.Hex(value["effectiveOptionsSha256"],64)&&new[]{"synthetic_only","observed_unqualified"}.Contains(value["providerQualification"] as string),"Exact unqualified observed DGK settings snapshot required.");
   foreach(string field in new[]{"coordinateFrame","exportVariant","triangleCompanions","featureCompanions"})
    Require(EntitySets.Canonical(value[field])==EntitySets.Canonical(parameters[field]),"Observed/planned DGK setting differs: "+field);
  }
  static Dictionary<string,object>[] Targets(OwnedDgkTargets.TargetLease lease,IContext context) {
   var inventory=EntitySets.Inventory(lease.Source.Expected);var result=new List<Dictionary<string,object>>();
   foreach(var key in lease.Source.Keys) {
    var item=inventory.Single(value=>Equals(value["nativeTypeCode"],key[0])&&Equals(value["nativeId"],key[1]));
    object raw;Require(item.TryGetValue("name",out raw)&&raw is string&&!string.IsNullOrWhiteSpace((string)raw),"DGK native target name missing.");
    string name=(string)raw,type=Type(key[0]);Require(!name.Any(c=>char.IsControl(c)||c=='\''),"Unsafe native selection name.");
    Require(Equals(item["type"],type)&&inventory.Count(value=>Equals(value["type"],type)&&Equals(value["name"],name))==1,"DGK type/name selection ambiguous.");
    Require(item.ContainsKey("levelNumber")&&item["levelNumber"] is int,"Target level missing.");
    var levels=lease.Source.Expected["levelsContainingEntities"] as IList;Require(levels!=null,"Level observation missing.");
    var active=levels.Cast<Dictionary<string,object>>().Where(level=>Equals(level["number"],item["levelNumber"])).ToArray();
    Require(active.Length==1&&(Equals(active[0]["activeRaw"],1)||Equals(active[0]["activeRaw"],true)),"DGK target level must already be active.");
    result.Add(new Dictionary<string,object>{{"entityKey",key},{"type",type},{"name",name},{"levelNumber",item["levelNumber"]},{"compositeId",context.Composite(key[0],key[1])}});
   }
   Require(result.All(item=>item["compositeId"] is int&&(int)item["compositeId"]>0)&&result.Select(item=>item["compositeId"]).Distinct().Count()==result.Count,"DGK native item mapping absent/ambiguous.");return result.ToArray();
  }
  static void Check(OwnedDgkTargets.TargetLease lease,IContext context,Dictionary<string,object>[] targets,int selected,Dictionary<string,object> settings,Dictionary<string,string> saved) {
   lease.RecheckDependencies();var expected=targets.Take(selected).Select(item=>new Dictionary<string,object>{{"type",item["type"]},{"name",item["name"]}}).ToArray();
   var expectedKeys=lease.Source.Keys.Take(selected).ToArray();
   for(int pass=0;pass<2;pass++) {
    Require(!context.Busy(),"PowerShape context is busy; no DGK action.");
    var fresh=context.Observation();
    foreach(string field in new[]{"contextBefore","contextAfter"})Require(EntitySets.Canonical(fresh[field])==EntitySets.Canonical(new[]{selected,0}),"DGK selection/world count changed.");
    foreach(string field in new[]{"selectionBefore","selectionAfter"})Require(SelectionDigest(fresh[field])==SelectionDigest(expected),"Exact own DGK selection names changed.");
    Require(KeyDigest(context.SelectedKeys())==KeyDigest(expectedKeys),"Exact native DGK selected keys changed.");
    var normalized=Clone(fresh);normalized["contextBefore"]=normalized["contextAfter"]=new[]{0,0};normalized["selectionBefore"]=normalized["selectionAfter"]=new object[0];
    EntitySets.CheckObserved(lease.Source.Expected,normalized,lease.Source.Keys,lease.Source.Expires,DateTime.UtcNow);
    var now=context.ExportSettings();Settings(now,(Dictionary<string,object>)lease.Plan["parameters"]);Require(EntitySets.Canonical(now)==EntitySets.Canonical(settings),"Pinned DGK effective settings changed.");
    foreach(var item in targets) {var key=(int[])item["entityKey"];Require(context.Composite(key[0],key[1])==(int)item["compositeId"],"DGK native item mapping changed.");}
   }
   context.ConfirmDocument();Require(!context.Busy(),"PowerShape became busy before DGK action.");Require(DocumentBindings.WindowDigest(context.Windows())==(string)lease.Plan["windowContextSha256"],"DGK document windows changed.");
   var current=WorkingCopyOpen.SavedHashes(context.Windows());Require(saved.Count==current.Count&&saved.All(pair=>current.ContainsKey(pair.Key)&&current[pair.Key]==pair.Value),"Saved documents changed during DGK export.");
   Require(DateTime.UtcNow<lease.Source.Expires,"DGK export context expired.");
  }
  static void Empty(OwnedDgkTargets.TargetLease lease){Require(!Directory.EnumerateFileSystemEntries((string)lease.Plan["bundleDirectory"]).Any(),"DGK output appeared before export; no overwrite.");}
  static string Event(string folder,int index,string phase,object value){return NativeJournal.Freeze(Path.Combine(folder,index.ToString("D6")+"."+phase+".json"),value);}
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> arguments,IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_dgk_export_rejected_before_change"},{"operationId",id},{"exportAttempted",false},
    {"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"geometryVerified",false},
    {"nativeProviderQualified",false},{"nativeReturnCodeQualified",false},{"retryAllowed",false},{"copyFenceReleased",false}};
   NativeJournal journal=null;int calls=0;string fence=null;Dictionary<string,object> sourceProof=null;
   try {
    OwnedGeometryExport.Validate(arguments);Require(EntitySets.Hex(id,32),"DGK execution identity required.");var args=Clone(arguments);
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var lease=new OwnedDgkTargets.TargetLease(root,(string)args["targetId"],(string)args["targetSha256"]))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     sourceProof=new Dictionary<string,object>{{"requestId",lease.Plan["inspectionRequestId"]},{"sha256",lease.Plan["inspectionSha256"]}};
     var parameters=(Dictionary<string,object>)lease.Plan["parameters"];var settings=Clone(context.ExportSettings());Settings(settings,parameters);
     var targets=Targets(lease,context);var saved=WorkingCopyOpen.SavedHashes(context.Windows());Check(lease,context,targets,0,settings,saved);lease.Recheck();
     var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"operationId",id},{"operation","export_owned_dgk"},
      {"copyId",lease.Plan["copyId"]},{"ownershipSha256",lease.Plan["ownershipSha256"]},{"parameters",parameters},{"artifactManifest",lease.Plan["artifactManifest"]},
      {"bundleDirectory",lease.Plan["bundleDirectory"]},{"targetPath",lease.Plan["targetPath"]},{"targets",targets},{"settings",settings},
      {"windowContextSha256",lease.Plan["windowContextSha256"]},{"savedFileHashesBefore",saved},{"createdUtc",DateTime.UtcNow.ToString("o")},
      {"qualification","Internal DGK callback sequence only; COM/settings/native return/DGK geometry unqualified"}};
     string planFolder=Path.Combine(root,"dgkexportexecutions");Directory.CreateDirectory(planFolder);OwnedCopies.NoReparse(planFolder);
     string planSha=NativeJournal.Freeze(Path.Combine(planFolder,id+".json"),plan);result["planSha256"]=planSha;result["plan"]=plan;result["copyId"]=plan["copyId"];
     journal=new NativeJournal(root,id,planSha);journal.Append("prepared",new Dictionary<string,object>{{"operation","export_owned_dgk"},{"targetCount",targets.Length}});
     fence=OwnedGeometryExportTargets.ExportFencePath(root,(string)plan["copyId"]);
     string fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",planSha},{"copyId",plan["copyId"]},{"ownershipSha256",plan["ownershipSha256"]},{"automaticReplayForbidden",true}});
     proofs.Json(fence,fenceSha);result["exportFenceSha256"]=fenceSha;
     string commandFolder=OwnedDgkTargets.Folder(root,(string)args["targetId"]),claimPath=Path.Combine(commandFolder,"export-once.json");
     string claimSha=NativeJournal.Freeze(claimPath,new Dictionary<string,object>{{"operationId",id},{"planSha256",planSha},{"targetId",args["targetId"]},{"automaticReplayForbidden",true}});proofs.Json(claimPath,claimSha);
     journal.Append("execution_claimed",new Dictionary<string,object>{{"targetId",args["targetId"]},{"automaticReplayForbidden",true}});Check(lease,context,targets,0,settings,saved);Empty(lease);
     var records=new List<Dictionary<string,object>>();journal.Append("command_started",new Dictionary<string,object>{{"selectionAndExport",true},{"targetCount",targets.Length}});
     for(int index=0;index<targets.Length;index++) {
      var target=targets[index];string started=Event(commandFolder,index,"started",new{operationId=id,planSha256=planSha,index=index,kind="add_selection",target=target});
      Check(lease,context,targets,index,settings,saved);Empty(lease);var key=(int[])target["entityKey"];calls++;result["contextChangeAttempted"]=true;
      context.AddSelection(key[0],key[1],(string)target["name"]);
      string returned=Event(commandFolder,index,"returned",new{operationId=id,planSha256=planSha,index=index,kind="add_selection",startedSha256=started,callbackReturned=true});
      records.Add(new Dictionary<string,object>{{"kind","add_selection"},{"index",index},{"startedSha256",started},{"returnedSha256",returned}});Check(lease,context,targets,index+1,settings,saved);Empty(lease);
     }
     int exportIndex=targets.Length;string exportStart=Event(commandFolder,exportIndex,"started",new{operationId=id,planSha256=planSha,index=exportIndex,kind="export_dgk",path=plan["targetPath"]});
     Check(lease,context,targets,targets.Length,settings,saved);Empty(lease);calls++;result["exportAttempted"]=true;
     context.ExportDgk((string)plan["targetPath"],Clone(parameters));
     string exportReturn=Event(commandFolder,exportIndex,"returned",new{operationId=id,planSha256=planSha,index=exportIndex,kind="export_dgk",startedSha256=exportStart,callbackReturned=true});
     records.Add(new Dictionary<string,object>{{"kind","export_dgk"},{"index",exportIndex},{"startedSha256",exportStart},{"returnedSha256",exportReturn}});Check(lease,context,targets,targets.Length,settings,saved);
     using(var bundle=new DgkBundleFiles.Lease(root,(string)plan["bundleDirectory"],lease.BundlePlan)) {
      int clearIndex=targets.Length+1;string clearStart=Event(commandFolder,clearIndex,"started",new{operationId=id,planSha256=planSha,index=clearIndex,kind="clear_own_selection"});
      Check(lease,context,targets,targets.Length,settings,saved);bundle.Recheck();calls++;context.ClearOwnSelection();
      string clearReturn=Event(commandFolder,clearIndex,"returned",new{operationId=id,planSha256=planSha,index=clearIndex,kind="clear_own_selection",startedSha256=clearStart,callbackReturned=true});
      records.Add(new Dictionary<string,object>{{"kind","clear_own_selection"},{"index",clearIndex},{"startedSha256",clearStart},{"returnedSha256",clearReturn}});
      Check(lease,context,targets,0,settings,saved);bundle.Recheck();
      journal.Append("command_returned",new Dictionary<string,object>{{"callbackCalls",calls},{"exportCallbackReturned",true},{"nativeReturnCodeQualified",false}});
      string summary=NativeJournal.Freeze(Path.Combine(commandFolder,"export-summary.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",planSha},{"commands",records},{"bundle",bundle.Report},{"geometryVerified",false}});
      journal.Append("executed_unverified",new Dictionary<string,object>{{"summarySha256",summary},{"independentVerificationRequired",true}});
      result["state"]="owned_dgk_export_executed_unverified";result["summarySha256"]=summary;result["selectionRestoredObserved"]=true;result["bundle"]=bundle.Report;
     }
    }
   }catch(Exception error) {
    result["error"]=error.Message;bool unknown=calls>0||journal!=null&&journal.State=="command_started";
    result["state"]=unknown?"owned_dgk_export_outcome_unknown":"owned_dgk_export_rejected_before_change";
    if(journal!=null)try {
     if(new[]{"prepared","execution_claimed"}.Contains(journal.State))journal.Append("rejected_before_change",new Dictionary<string,object>{{"contextChangeAttempted",false},{"evidence",new[]{sourceProof}}});
     else if(new[]{"command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"callbackCallsAttempted",calls},{"error",error.Message}});
    }catch(Exception failure){result["journalError"]=failure.Message;}
   }
   result["callbackCallsAttempted"]=calls;result["copyExportFenced"]=fence==null?(object)null:File.Exists(fence);if(journal!=null)result["recordedJournalState"]=journal.State;
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
