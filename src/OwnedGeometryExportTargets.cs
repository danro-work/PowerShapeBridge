using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Filesystem preparation only. No selection/Export/Exec/COM call or execution authority.
 internal static class OwnedGeometryExportTargets {
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  internal static void Validate(Dictionary<string,object> args){string[] keys={"bindingId","bindingSha256","entitySetId","entitySetSha256"};Require(args.Count==4&&keys.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],k.EndsWith("Sha256",StringComparison.Ordinal)?64:32)),"Exact owned binding/entity-set references required.");}
  internal static string Folder(string root,string id){Require(EntitySets.Hex(id,32),"Export identity required.");string path=Path.Combine(OwnedCopies.LocalPath(root.TrimEnd('\\','/')),"geometryexports",id);OwnedCopies.NoReparse(path);return path;}
  internal static string TargetPath(string root,string id){return Path.Combine(Folder(root,id),"Geometry_"+id+".dmt");}
  static Dictionary<string,object> Fence(string root,Dictionary<string,object> binding,Dictionary<string,object> source,OwnedOperationPlans.Proofs proofs) {
   string path=OwnedTranslation.FencePath(root,(string)binding["copyId"]);OwnedCopies.NoReparse(path);Require(!Directory.Exists(path),"Fence path is a directory.");if(!File.Exists(path))return new Dictionary<string,object>{{"present",false}};
   var record=proofs.Json(path,null);Require(record.Count==6&&record.ContainsKey("operationId")&&EntitySets.Hex(record["operationId"],32)&&record.ContainsKey("planSha256")&&EntitySets.Hex(record["planSha256"],64)&&Equals(record["schemaVersion"],1)&&Equals(record["copyId"],binding["copyId"])&&Equals(record["ownershipSha256"],binding["ownershipSha256"])&&Equals(record["automaticReplayForbidden"],true),"Unknown/foreign copy fence; export preparation forbidden.");
   string journalFolder=Path.Combine(root,"operations",(string)record["operationId"]);OwnedCopies.NoReparse(journalFolder);foreach(string eventPath in Directory.GetFiles(journalFolder,"*.json"))proofs.Json(eventPath,null);
   var journal=NativeJournal.ForVerification(root,(string)record["operationId"],(string)record["planSha256"]);Require(journal.State=="executed_unverified"&&journal.CommandReturnCode==1,"Uncertain/unfinished mutation prohibits export preparation.");
   string id=(string)record["operationId"];var plan=proofs.Json(Path.Combine(root,"rotationexecutions",id+".json"),(string)record["planSha256"],16000000);
   Require(Equals(plan["schemaVersion"],2)&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"rotate_owned_entities")&&Equals(plan["copyId"],binding["copyId"])&&Equals(plan["ownershipSha256"],binding["ownershipSha256"]),"Only a linked complete rotation fence may be observed.");
   Require(EntitySets.Hex(plan["rotationPlanId"],32),"Rotation claim identity required.");var claim=proofs.Json(Path.Combine(root,"rotationplans",(string)plan["rotationPlanId"]+".execution-once.json"),null);Require(Equals(claim["operationId"],id)&&Equals(claim["planSha256"],record["planSha256"])&&Equals(claim["copyId"],binding["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Original rotation claim differs.");
   var targets=((IList)plan["targets"]).Cast<Dictionary<string,object>>().ToArray();Require(targets.Length>0&&targets.Length<=128&&Equals(journal.CommandReturnedDetails["commandCalls"],targets.Length)&&Equals(journal.CommandReturnedDetails["allTargetsReturnedSuccess"],true),"Incomplete aggregate rotation return.");
   var summary=proofs.Json(Path.Combine(root,"rotationcommands",id,"summary.json"),(string)journal.LastDetails["summarySha256"],16000000);Require(Equals(summary["operationId"],id)&&Equals(summary["planSha256"],record["planSha256"])&&Equals(summary["geometryVerified"],false),"Rotation summary differs.");
   var commands=((IList)summary["commands"]).Cast<Dictionary<string,object>>().ToArray();Require(commands.Length==targets.Length,"Missing per-target rotation returns.");
   for(int index=0;index<targets.Length;index++) {
    var command=commands[index];string prefix=Path.Combine(root,"rotationcommands",id,index.ToString("D6"));var started=proofs.Json(prefix+".started.json",(string)command["startedSha256"]);var returned=proofs.Json(prefix+".returned.json",(string)command["returnedSha256"]);
    Require(Equals(command["index"],index)&&Equals(command["returnCode"],1)&&EntitySets.Canonical(command["entityKey"])==EntitySets.Canonical(targets[index]["entityKey"])&&Equals(started["operationId"],id)&&Equals(started["index"],index)&&Equals(started["planSha256"],record["planSha256"])&&EntitySets.Canonical(started["entityKey"])==EntitySets.Canonical(targets[index]["entityKey"])&&Equals(started["compositeId"],targets[index]["compositeId"])&&Equals(returned["operationId"],id)&&Equals(returned["index"],index)&&Equals(returned["planSha256"],record["planSha256"])&&Equals(returned["startedSha256"],command["startedSha256"])&&Equals(returned["returnCode"],1),"Incomplete/foreign per-target rotation return.");
   }
   Require(EntitySets.Expiry(source["timestampUtc"])>journal.CommandReturnedUtc,"Export observation must follow aggregate rotation return.");
   // A complete-return fence may be observed, but this preparation never resolves/removes it.
   return new Dictionary<string,object>{{"present",true},{"operationId",record["operationId"]},{"planSha256",record["planSha256"]},{"fenceSha256",Program.Hash(path)},{"journalState",journal.State},{"summarySha256",journal.LastDetails["summarySha256"]}};
  }
  static void Empty(string path){OwnedCopies.NoReparse(path);Require(!File.Exists(path)&&!Directory.Exists(path),"Export output/claim exists; no overwrite or replay.");}
  internal sealed class Dependencies {
   internal readonly Dictionary<string,object> Binding,Expected;internal readonly int[][] Keys;internal readonly DateTime Expires;
   internal Dependencies(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs) {
    Validate(args);var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};var loaded=DocumentBindings.Load(root,bindingArgs);Binding=proofs.Json(Path.Combine(root,"documentbindings",(string)args["bindingId"]+".json"),(string)args["bindingSha256"]);Require(EntitySets.Canonical(loaded)==EntitySets.Canonical(Binding),"Binding changed during load.");
    Expected=proofs.Json(Path.Combine(root,"responses",(string)Binding["inspectionRequestId"]+".json"),(string)Binding["inspectionSha256"],16000000);var set=proofs.Json(Path.Combine(root,"entitysets",(string)args["entitySetId"]+".json"),(string)args["entitySetSha256"]);EntitySets.ValidatePrepare(set);var source=proofs.Json(Path.Combine(root,"responses",(string)set["inspectionRequestId"]+".json"),(string)set["inspectionSha256"],16000000);Keys=EntitySets.Keys(set["entityKeys"]);Require(Keys.Length>0&&Keys.Length<=128&&Keys.All(k=>k[0]==40||k[0]==21),"DMT qualification targets must be solids or surfaces.");Expires=new[]{EntitySets.Expiry(Binding["expiresUtc"]),EntitySets.Expiry(set["expiresUtc"])}.Min();
    EntitySets.CheckObserved(source,Expected,Keys,Expires,DateTime.UtcNow);Require(EntitySets.ObservedDigest(Expected)==(string)Binding["observedPropertiesSha256"],"Binding observation differs.");
    foreach(string key in new[]{"contextBefore","contextAfter"})Require(EntitySets.Canonical(Expected[key])==EntitySets.Canonical(new[]{0,0}),"World/empty-selection snapshot required.");
    foreach(string key in new[]{"selectionBefore","selectionAfter"})Require(((IList)Expected[key]).Count==0,"Empty selection required.");
   }
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args) {
   var result=new Dictionary<string,object>{{"state","owned_geometry_export_target_rejected"},{"targetId",id},{"executionEnabled",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"exportAttempted",false},{"powerShapeConnectionAttempted",false},{"retryAllowed",false}};
   try {
    Validate(args);string folder=Folder(root,id);Require(!Directory.Exists(folder)&&!File.Exists(folder),"Export identity exists; no overwrite.");
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var d=new Dependencies(root,args,proofs);var binding=d.Binding;var source=d.Expected;var keys=d.Keys;var expiry=d.Expires;
     using(var copy=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"])) {
      copy.CheckPath((string)((Dictionary<string,object>)source["model"])["path"]);copy.Recheck();var fence=Fence(root,binding,source,proofs);
      string parent=Path.GetDirectoryName(folder);Directory.CreateDirectory(parent);OwnedCopies.NoReparse(parent);
      using(var rootPin=new OwnedDirectoryLease(root))using(var parentPin=new OwnedDirectoryLease(parent)) {
       result["localArtifactsMayExist"]=true;NativeJournal.Freeze(folder+".reservation.json",new Dictionary<string,object>{{"schemaVersion",1},{"targetId",id},{"format","dmt"},{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]},{"automaticReplayForbidden",true}});Directory.CreateDirectory(folder);Require(Directory.GetFileSystemEntries(folder).Length==0,"Export folder not empty.");
       using(var folderPin=new OwnedDirectoryLease(folder)) {
        string path=TargetPath(root,id);Empty(path);
        var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"targetId",id},{"operation","export_owned_geometry"},{"format","dmt"},{"selectionPolicy","explicit_target_set; separately qualified full-mode executor required"},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",expiry.ToString("o")},{"executionEnabled",false},{"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"inspectionRequestId",binding["inspectionRequestId"]},{"inspectionSha256",binding["inspectionSha256"]},{"nativeObservedPropertiesSha256",binding["observedPropertiesSha256"]},{"windowContextSha256",binding["windowContextSha256"]},{"entityKeys",keys},{"workingPath",copy.WorkingPath},{"workingSha256",copy.Inspection["workingSha256"]},{"checkpointPath",copy.CheckpointPath},{"checkpointSha256",copy.CheckpointSha},{"targetPath",path},{"directoryIdentity",folderPin.Identity},{"fenceObservation",fence},{"qualification","Disabled own geometry export target; no mesh parser, selection/export execution or topology/convention proof"}};
        string sha=NativeJournal.Freeze(Path.Combine(folder,"target.json"),plan);rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();copy.Recheck();Require(EntitySets.Canonical(Fence(root,binding,source,proofs))==EntitySets.Canonical(fence),"Copy fence changed during preparation.");Require(DateTime.UtcNow<expiry,"Export preparation expired.");result["state"]="owned_geometry_export_target_prepared_files";result["targetSha256"]=sha;result["target"]=plan;
       }
      }
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal sealed class TargetLease : IDisposable {
   internal readonly Dictionary<string,object> Plan;internal readonly OwnedCopies.OwnershipLease Copy;internal readonly Dependencies Source;
   readonly string root,id;readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();OwnedDirectoryLease rootPin,parentPin,folderPin;
   internal TargetLease(string bridgeRoot,string targetId,string sha) {
    root=OwnedCopies.LocalPath(bridgeRoot.TrimEnd('\\','/'));id=targetId;
    try {
     Require(EntitySets.Hex(sha,64),"Target SHA required.");string folder=Folder(root,id);rootPin=new OwnedDirectoryLease(root);parentPin=new OwnedDirectoryLease(Path.GetDirectoryName(folder));folderPin=new OwnedDirectoryLease(folder);Plan=proofs.Json(Path.Combine(folder,"target.json"),sha);
     string[] fields={"bindingId","bindingSha256","entitySetId","entitySetSha256","schemaVersion","targetId","operation","format","selectionPolicy","preparedUtc","expiresUtc","executionEnabled","copyId","ownershipSha256","inspectionRequestId","inspectionSha256","nativeObservedPropertiesSha256","windowContextSha256","entityKeys","workingPath","workingSha256","checkpointPath","checkpointSha256","targetPath","directoryIdentity","fenceObservation","qualification"};
     Require(Plan.Count==fields.Length&&fields.All(Plan.ContainsKey)&&Equals(Plan["schemaVersion"],1)&&Equals(Plan["targetId"],id)&&Equals(Plan["operation"],"export_owned_geometry")&&Equals(Plan["format"],"dmt")&&Equals(Plan["executionEnabled"],false)&&Equals(Plan["selectionPolicy"],"explicit_target_set; separately qualified full-mode executor required")&&Equals(Plan["targetPath"],TargetPath(root,id))&&EntitySets.Canonical(Plan["directoryIdentity"])==EntitySets.Canonical(folderPin.Identity),"Invalid disabled export target/directory identity.");
     Source=new Dependencies(root,new[]{"bindingId","bindingSha256","entitySetId","entitySetSha256"}.ToDictionary(k=>k,k=>Plan[k]),proofs);
     foreach(string key in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","windowContextSha256"})Require(Equals(Plan[key],Source.Binding[key]),"Export binding dependency differs.");
     Require(Equals(Plan["nativeObservedPropertiesSha256"],Source.Binding["observedPropertiesSha256"])&&EntitySets.Canonical(Plan["entityKeys"])==EntitySets.Canonical(Source.Keys)&&EntitySets.Expiry(Plan["expiresUtc"])==Source.Expires&&EntitySets.Expiry(Plan["preparedUtc"])>=EntitySets.Expiry(Source.Expected["timestampUtc"]),"Export observation/targets/expiry differ.");
     Copy=OwnedCopies.Hold(root,(string)Plan["copyId"],(string)Plan["ownershipSha256"]);Require(Equals(Plan["workingPath"],Copy.WorkingPath)&&Equals(Plan["workingSha256"],Copy.Inspection["workingSha256"])&&Equals(Plan["checkpointPath"],Copy.CheckpointPath)&&Equals(Plan["checkpointSha256"],Copy.CheckpointSha),"Export copy files differ.");Copy.CheckPath((string)((Dictionary<string,object>)Source.Expected["model"])["path"]);
     var reservation=proofs.Json(folder+".reservation.json",null);Require(reservation.Count==6&&Equals(reservation["schemaVersion"],1)&&Equals(reservation["targetId"],id)&&Equals(reservation["format"],"dmt")&&Equals(reservation["bindingId"],Plan["bindingId"])&&Equals(reservation["bindingSha256"],Plan["bindingSha256"])&&Equals(reservation["automaticReplayForbidden"],true),"Export reservation differs.");Recheck();
    }catch{Dispose();throw;}
   }
   internal void Recheck() {
    rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();Copy.Recheck();EntitySets.CheckObserved(Source.Expected,Source.Expected,Source.Keys,Source.Expires,DateTime.UtcNow);
    Require(EntitySets.Canonical(Fence(root,Source.Binding,Source.Expected,proofs))==EntitySets.Canonical(Plan["fenceObservation"]),"Export copy fence changed.");Empty((string)Plan["targetPath"]);Empty(Path.Combine(Folder(root,id),"export-once.json"));
   }
   public void Dispose(){if(Copy!=null)Copy.Dispose();proofs.Dispose();if(folderPin!=null)folderPin.Dispose();if(parentPin!=null)parentPin.Dispose();if(rootPin!=null)rootPin.Dispose();}
  }
 }
}
