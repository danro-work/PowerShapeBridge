using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Filesystem-only recovery. Never resolves the origin or attaches to CAD.
 internal static class CheckpointRecovery {
  internal const string Qualification="A NEW owned copy of protected checkpoint saved bytes only. Original operation/outcome/claims/fences stay unchanged; unsaved state is not recovered. No CAD, OPEN, Save, close, inverse or automatic replay.";
  static readonly string[] Folders={"ownedexecutions","saveexecutions","copyopenplans"};
  internal static void Validate(Dictionary<string,object> args,bool materialize) {
   string id=materialize?"recoveryId":"operationId",sha=materialize?"recoverySha256":"operationPlanSha256";
   if(args.Count!=2||!args.ContainsKey(id)||!EntitySets.Hex(args[id],32)||!args.ContainsKey(sha)||!EntitySets.Hex(args[sha],64))throw new ArgumentException("Invalid checkpoint recovery parameters.");
  }
  static string Artifact(string root,string folder,string id,string suffix=".json") {
   if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid recovery identity.");string path=Path.Combine(root,folder,id+suffix);OwnedCopies.NoReparse(path);return path;
  }
  static Dictionary<string,object> Origin(string root,string id,string sha) {
   string[] paths=Folders.Select(candidate=>Artifact(root,candidate,id)).Where(File.Exists).ToArray();
   if(paths.Length!=1)throw new IOException("One pinned owned origin plan required; legacy activation is not a checkpoint source.");
   var plan=NativeJournal.VerifiedJson(paths[0],sha,65536);string folder=Path.GetFileName(Path.GetDirectoryName(paths[0]));
   string operation=plan.ContainsKey("operation")?plan["operation"] as string:null;
   if(!Equals(plan["operationId"],id)||!Equals(plan["schemaVersion"],1)||!EntitySets.Hex(plan["copyId"],32)||!EntitySets.Hex(plan["ownershipSha256"],64)||
    !(folder=="ownedexecutions"&&operation=="translate_owned_solid"||folder=="saveexecutions"&&operation=="save_owned_document_as"||folder=="copyopenplans"&&new[]{"open_working_copy","open_owned_saved_result"}.Contains(operation)))throw new IOException("Origin plan identity/operation differs.");
   return new Dictionary<string,object>{{"operationId",id},{"operationPlanSha256",sha},{"planFolder",folder},{"copyId",plan["copyId"]},{"ownershipSha256",plan["ownershipSha256"]}};
  }
  static Dictionary<string,object> Checkpoint(OwnedCopies.OwnershipLease lease) {
   var owner=(Dictionary<string,object>)lease.Inspection["ownership"];return (Dictionary<string,object>)owner["checkpoint"];
  }
  static Dictionary<string,string> OriginalArtifacts(string root,Dictionary<string,object> origin,OwnedCopies.OwnershipLease lease) {
   var paths=new List<string>{Artifact(root,(string)origin["planFolder"],(string)origin["operationId"]),Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"ownership.json"),lease.WorkingPath,lease.CheckpointPath};
   string journal=Path.Combine(root,"operations",(string)origin["operationId"]);
   if(Directory.Exists(journal))paths.AddRange(Directory.GetFiles(journal,"*",SearchOption.AllDirectories));
   foreach(string name in new[]{"mutation-fence.json","open-once.json"}){string path=Path.Combine(Path.GetDirectoryName(lease.WorkingPath),name);if(File.Exists(path))paths.Add(path);}
   // The origin plan can contain an operation-specific claim outside its copy.
   var plan=NativeJournal.VerifiedJson(paths[0],(string)origin["operationPlanSha256"],65536);
   if(Equals(plan["operation"],"save_owned_document_as")&&plan.ContainsKey("targetId")&&EntitySets.Hex(plan["targetId"],32)){string path=Path.Combine(root,"savedresults",(string)plan["targetId"],"save-once.json");if(File.Exists(path))paths.Add(path);}
   if(Equals(plan["operation"],"translate_owned_solid")&&plan.ContainsKey("ownedOperationId")&&plan.ContainsKey("ownedOperationSha256")) {
    var packet=NativeJournal.VerifiedJson(Artifact(root,"ownedoperations",(string)plan["ownedOperationId"]),(string)plan["ownedOperationSha256"],65536);
    if(!EntitySets.Hex(packet["operationPlanId"],32))throw new IOException("Origin local plan identity invalid.");
    string path=Artifact(root,"operationplans",(string)packet["operationPlanId"],".execution-once.json");if(File.Exists(path))paths.Add(path);
   }
   foreach(string path in paths)OwnedCopies.NoReparse(path);return paths.Distinct().ToDictionary(path=>path,path=>Program.Hash(path));
  }
  static void Unchanged(Dictionary<string,string> hashes){if(hashes.Any(pair=>!File.Exists(pair.Key)||Program.Hash(pair.Key)!=pair.Value))throw new IOException("Original artifacts changed; no new authority granted.");}
  static Dictionary<string,object> Result(string state){return new Dictionary<string,object>{{"state",state},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"powerShapeConnectionAttempted",false},{"originalOutcomeResolved",false},{"retryAllowed",false},{"qualification",Qualification}};}
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args) {
   Validate(args,false);string path=Artifact(root,"checkpointrecoveries",id);if(File.Exists(path))throw new IOException("Recovery intent already exists; no overwrite.");
   Directory.CreateDirectory(Path.Combine(root,"operations"));
   using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    var origin=Origin(root,(string)args["operationId"],(string)args["operationPlanSha256"]);
    using(var lease=OwnedCopies.Hold(root,(string)origin["copyId"],(string)origin["ownershipSha256"])) {
     lease.Recheck();var hashes=OriginalArtifacts(root,origin,lease);DateTime created=DateTime.UtcNow;
     var plan=new Dictionary<string,object>{{"schemaVersion",1},{"recoveryId",id},{"operation","copy_owned_checkpoint"},{"bridgeVersion",Program.ReleaseVersion},{"preparedUtc",created.ToString("o")},{"expiresUtc",created.AddSeconds(300).ToString("o")},{"cadExecutionEnabled",false},{"originalOutcomeResolved",false},{"origin",origin},{"checkpoint",Checkpoint(lease)}};
     Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,plan);lease.Recheck();Unchanged(hashes);
     var result=Result("checkpoint_recovery_prepared_files");result["recoveryId"]=id;result["recoverySha256"]=sha;result["recovery"]=plan;result["originalArtifactsUnchanged"]=true;result["originalArtifactHashes"]=hashes;return result;
    }
   }
  }
  internal static Dictionary<string,object> Create(string root,string id,Dictionary<string,object> args,Action<string> interruption=null) {
   Validate(args,true);string recoveryId=(string)args["recoveryId"],recoverySha=(string)args["recoverySha256"];
   var result=Result("checkpoint_copy_rejected_before_creation");result["copyId"]=id;bool claimed=false;
   try {
    string planPath=Artifact(root,"checkpointrecoveries",recoveryId);var recovery=NativeJournal.VerifiedJson(planPath,recoverySha,65536);
    if(recovery.Count!=10||!Equals(recovery["schemaVersion"],1)||!Equals(recovery["recoveryId"],recoveryId)||!Equals(recovery["operation"],"copy_owned_checkpoint")||!Equals(recovery["cadExecutionEnabled"],false)||!Equals(recovery["originalOutcomeResolved"],false)||DateTime.UtcNow>=EntitySets.Expiry(recovery["expiresUtc"])||EntitySets.Expiry(recovery["preparedUtc"])>DateTime.UtcNow.AddSeconds(5)||EntitySets.Expiry(recovery["expiresUtc"])>EntitySets.Expiry(recovery["preparedUtc"]).AddSeconds(300))throw new IOException("Invalid/expired recovery intent.");
    var recorded=(Dictionary<string,object>)recovery["origin"];var origin=Origin(root,(string)recorded["operationId"],(string)recorded["operationPlanSha256"]);if(EntitySets.Canonical(recorded)!=EntitySets.Canonical(origin))throw new IOException("Origin changed since preparation.");
    if(!EntitySets.Hex(id,32)||id==recoveryId||Equals(id,origin["operationId"])||Equals(id,origin["copyId"]))throw new IOException("A distinct NEW recovery copy identity required.");
    string claim=Artifact(root,"checkpointrecoveries",recoveryId,".copy-once.json");if(File.Exists(claim))throw new IOException("Recovery intent already claimed; inspect original reply, never retry.");
    Directory.CreateDirectory(Path.Combine(root,"operations"));
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var lease=OwnedCopies.Hold(root,(string)origin["copyId"],(string)origin["ownershipSha256"])) {
     if(EntitySets.Canonical(Checkpoint(lease))!=EntitySets.Canonical(recovery["checkpoint"]))throw new IOException("Checkpoint role/identity differs from recovery intent.");
     lease.Recheck();var hashes=OriginalArtifacts(root,origin,lease);
     var creation=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"operation","materialize_owned_checkpoint"},{"recoveryId",recoveryId},{"recoverySha256",recoverySha},{"origin",origin},{"checkpoint",Checkpoint(lease)},{"cadExecutionEnabled",false},{"originalOutcomeResolved",false},{"createdUtc",DateTime.UtcNow.ToString("o")}};
     string creationPath=Artifact(root,"checkpointcopyplans",id);Directory.CreateDirectory(Path.GetDirectoryName(creationPath));string creationSha=NativeJournal.Freeze(creationPath,creation);
     NativeJournal.Freeze(claim,new Dictionary<string,object>{{"operationId",id},{"recoveryId",recoveryId},{"planSha256",creationSha},{"automaticReplayForbidden",true}});claimed=true;
     if(interruption!=null)interruption("after_claim");
     var copy=OwnedCopies.CreateInterrupted(root,id,lease.CheckpointPath,lease.CheckpointSha,2,interruption);lease.Recheck();Unchanged(hashes);
     if(interruption!=null)interruption("after_copy");
     var inspected=OwnedCopies.Inspect(root,id,(string)copy["ownershipSha256"]);if(!Equals(inspected["checkpointVerified"],true)||!Equals(inspected["workingFileChanged"],false))throw new IOException("Recovered byte copy failed verification.");
     result=Result("checkpoint_working_copy_prepared_disk");foreach(var pair in copy)if(pair.Key!="state"&&pair.Key!="qualification")result[pair.Key]=pair.Value;
     result["recoveryId"]=recoveryId;result["recoverySha256"]=recoverySha;result["creationPlan"]=creation;result["creationPlanSha256"]=creationSha;
     result["originalArtifactsUnchanged"]=true;result["originalArtifactHashes"]=hashes;
     string proof=Artifact(root,"checkpointcopyplans",id,".result.json");string proofSha=NativeJournal.Freeze(proof,result);result["materializationProofSha256"]=proofSha;
    }
   }catch(Exception error){result=Result(claimed?"checkpoint_copy_outcome_unknown":"checkpoint_copy_rejected_before_creation");result["copyId"]=id;result["localArtifactsMayExist"]=claimed;result["error"]=error.Message;}
   return result;
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args,bool materialize) {
   var result=Result("checkpoint_recovery_rejected");
   try{var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.checkpointRecoveryExperimental)throw new InvalidOperationException("Checkpoint recovery disabled.");result=materialize?Create(Program.Root,id,args):Prepare(Program.Root,id,args);}
   catch(Exception error){result["error"]=error.Message;result["localArtifactsMayExist"]=true;}
   return result;
  }
 }
}
