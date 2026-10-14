using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Filesystem-only target preparation. No Exec/Save/Open/COM.
 internal static class OwnedSaveTargets {
  internal const string Qualification="Prepared owned SaveAs target only; no CAD command or later execution authorization. Source binding/receipt/files must be rechecked in one retained COM context immediately before the single SaveAs. Unknown outcomes are never replayed.";
  internal static string Folder(string root,string id) {
   if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid SaveAs target identity.");
   string path=Path.Combine(OwnedCopies.LocalPath(root.TrimEnd('\\','/')),"savedresults",id);
   OwnedCopies.NoReparse(path);return path;
  }
  internal static string ResultPath(string root,string id){return Path.Combine(Folder(root,id),"Result_"+id+".psmodel");}
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> parameters) {
   var result=new Dictionary<string,object>{{"state","owned_save_target_rejected"},{"executionEnabled",false},
    {"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},
    {"powerShapeConnectionAttempted",false},{"qualification",Qualification}};
   try {
    Config config=Program.ReadConfig();
    if(!config.IsGeneralCore||!config.ownedSaveTargetsExperimental)throw new InvalidOperationException("Experimental SaveAs targets disabled.");
    result=Prepare(Program.Root,id,parameters);result["powerShapeConnectionAttempted"]=false;
   }catch(Exception error){result["error"]=error.Message;result["targetId"]=id;result["localArtifactsMayExist"]=true;}
   result["bridgeVersion"]=Program.ReleaseVersion;result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static void NoFence(string root,string copyId) {
   OwnedGeometryExportTargets.NoExportFence(root,copyId);
   string fence=OwnedTranslation.FencePath(root,copyId);OwnedCopies.NoReparse(fence);
   if(File.Exists(fence)||Directory.Exists(fence))throw new InvalidOperationException("Owned copy is fenced; inspect original journal without retry.");
  }
  static void EmptyResult(string path) {
   OwnedCopies.NoReparse(path);
   if(File.Exists(path)||Directory.Exists(path))throw new IOException("SaveAs target already exists; overwrite is forbidden.");
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> parameters) {
   DocumentBindings.ValidateHandle(parameters);string folder=Folder(root,id);
   if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("SaveAs target identity exists; no overwrite or replay.");
   using(var proofs=new OwnedOperationPlans.Proofs()) {
    var binding=proofs.Json(Path.Combine(root,"documentbindings",(string)parameters["bindingId"]+".json"),(string)parameters["bindingSha256"]);
    // Load enforces the existing exact native binding schema and minimum expiry.
    DocumentBindings.Load(root,parameters);
    var expected=proofs.Json(Path.Combine(root,"responses",(string)binding["inspectionRequestId"]+".json"),(string)binding["inspectionSha256"],16777216);
    DateTime expiry=EntitySets.Expiry(binding["expiresUtc"]);
    EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
    if(EntitySets.ObservedDigest(expected)!=(string)binding["observedPropertiesSha256"])throw new IOException("Binding observation digest differs.");
    using(var copy=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"])) {
     var model=expected["model"] as Dictionary<string,object>;
     if(model==null)throw new IOException("Source model observation unavailable.");
     copy.CheckPath((string)model["path"]);copy.Recheck();NoFence(root,(string)binding["copyId"]);
     string parent=Path.GetDirectoryName(folder);Directory.CreateDirectory(parent);OwnedCopies.NoReparse(parent);
     using(var rootPin=new OwnedDirectoryLease(root))using(var parentPin=new OwnedDirectoryLease(parent)) {
      string reservation=folder+".reservation.json";
      NativeJournal.Freeze(reservation,new Dictionary<string,object>{{"schemaVersion",1},{"targetId",id},
       {"bindingId",binding["bindingId"]},{"bindingSha256",parameters["bindingSha256"]},{"automaticReplayForbidden",true}});
      Directory.CreateDirectory(folder);OwnedCopies.NoReparse(folder);
      if(Directory.GetFileSystemEntries(folder).Length!=0)throw new IOException("New target directory is not empty.");
      using(var folderPin=new OwnedDirectoryLease(folder)) {
       string target=ResultPath(root,id);EmptyResult(target);copy.Recheck();
       if(DateTime.UtcNow>=expiry)throw new InvalidOperationException("Binding expired during target preparation.");
       var plan=new Dictionary<string,object>{{"schemaVersion",1},{"targetId",id},{"operation","save_owned_document_as"},
        {"bridgeVersion",Program.ReleaseVersion},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",binding["expiresUtc"]},
        {"executionEnabled",false},{"bindingId",binding["bindingId"]},{"bindingSha256",parameters["bindingSha256"]},
        {"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},
        {"inspectionRequestId",binding["inspectionRequestId"]},{"inspectionSha256",binding["inspectionSha256"]},
        {"nativeObservedPropertiesSha256",binding["observedPropertiesSha256"]},{"beforeModel",model},
        {"workingPath",copy.WorkingPath},{"workingSha256",copy.Inspection["workingSha256"]},
        {"checkpointPath",copy.CheckpointPath},{"checkpointSha256",copy.CheckpointSha},
        {"resultPath",target},{"directoryIdentity",folderPin.Identity}};
       string sha=NativeJournal.Freeze(Path.Combine(folder,"target.json"),plan);
       rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();copy.Recheck();
       return new Dictionary<string,object>{{"state","owned_save_target_prepared_files"},{"targetId",id},
        {"targetSha256",sha},{"target",plan},{"executionEnabled",false},{"contextChangeAttempted",false},
        {"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"qualification",Qualification}};
      }
     }
    }
   }
  }
  internal sealed class TargetLease : IDisposable {
   internal readonly Dictionary<string,object> Plan;
   internal readonly OwnedCopies.OwnershipLease Copy;
   internal readonly Dictionary<string,object> Expected;
   internal readonly DateTime Expires;
   readonly string root,id;readonly bool verification;
   readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
   OwnedDirectoryLease rootPin,parentPin,folderPin;
   internal TargetLease(string bridgeRoot,string targetId,string sha,bool forVerification=false) {
    root=OwnedCopies.LocalPath(bridgeRoot.TrimEnd('\\','/'));id=targetId;verification=forVerification;
    try {
     if(!EntitySets.Hex(sha,64))throw new ArgumentException("Invalid target SHA.");
     string folder=Folder(root,id);rootPin=new OwnedDirectoryLease(root);
     parentPin=new OwnedDirectoryLease(Path.GetDirectoryName(folder));folderPin=new OwnedDirectoryLease(folder);
     Plan=proofs.Json(Path.Combine(folder,"target.json"),sha);
     string[] fields={"schemaVersion","targetId","operation","bridgeVersion","preparedUtc","expiresUtc","executionEnabled",
      "bindingId","bindingSha256","copyId","ownershipSha256","inspectionRequestId","inspectionSha256",
      "nativeObservedPropertiesSha256","beforeModel","workingPath","workingSha256","checkpointPath","checkpointSha256","resultPath","directoryIdentity"};
     if(Plan.Count!=fields.Length||fields.Any(k=>!Plan.ContainsKey(k))||!Equals(Plan["schemaVersion"],1)||
        !Equals(Plan["targetId"],id)||!Equals(Plan["operation"],"save_owned_document_as")||!Equals(Plan["executionEnabled"],false)||
        !string.Equals((string)Plan["resultPath"],ResultPath(root,id),StringComparison.OrdinalIgnoreCase)||
        EntitySets.Canonical(Plan["directoryIdentity"])!=EntitySets.Canonical(folderPin.Identity))throw new IOException("Invalid target plan or directory identity.");
     var parameters=new Dictionary<string,object>{{"bindingId",Plan["bindingId"]},{"bindingSha256",Plan["bindingSha256"]}};
     var binding=proofs.Json(Path.Combine(root,"documentbindings",(string)Plan["bindingId"]+".json"),(string)Plan["bindingSha256"]);
     DocumentBindings.Load(root,parameters);Expires=EntitySets.Expiry(Plan["expiresUtc"]);
     foreach(string key in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","expiresUtc"})
      if(!Equals(Plan[key],binding[key]))throw new IOException("Target binding dependency differs.");
     if(!Equals(Plan["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"]))throw new IOException("Target native digest differs.");
     Expected=proofs.Json(Path.Combine(root,"responses",(string)Plan["inspectionRequestId"]+".json"),(string)Plan["inspectionSha256"],16777216);
     if(EntitySets.Canonical(Plan["beforeModel"])!=EntitySets.Canonical(Expected["model"])||EntitySets.ObservedDigest(Expected)!=(string)Plan["nativeObservedPropertiesSha256"])throw new IOException("Target source observation differs.");
     Copy=OwnedCopies.Hold(root,(string)Plan["copyId"],(string)Plan["ownershipSha256"]);
     if(!Equals(Plan["workingPath"],Copy.WorkingPath)||!Equals(Plan["checkpointPath"],Copy.CheckpointPath)||
        !Equals(Plan["workingSha256"],Copy.Inspection["workingSha256"])||!Equals(Plan["checkpointSha256"],Copy.CheckpointSha))throw new IOException("Target file dependencies differ.");
     Copy.CheckPath((string)((Dictionary<string,object>)Expected["model"])["path"]);
     if(verification)RecheckDependencies();else Recheck();
    }catch {Dispose();throw;}
   }
   internal void Recheck() {
    if(verification)throw new InvalidOperationException("Verification target lease cannot authorize SaveAs.");
    RecheckDependencies();
    NoFence(root,(string)Plan["copyId"]);EmptyResult((string)Plan["resultPath"]);
    string claim=Path.Combine(Folder(root,id),"save-once.json");OwnedCopies.NoReparse(claim);
    if(File.Exists(claim)||Directory.Exists(claim))throw new InvalidOperationException("SaveAs target claimed; no replay.");
   }
   internal void RecheckDependencies() {
    rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();Copy.Recheck();
    // Old execution proofs may be verified later; they never authorize another
    // save. Fresh independent read leases are checked separately by the verifier.
    EntitySets.CheckObserved(Expected,Expected,new int[0][],Expires,verification?EntitySets.Expiry(Plan["preparedUtc"]):DateTime.UtcNow);
   }
   public void Dispose(){if(Copy!=null)Copy.Dispose();proofs.Dispose();if(folderPin!=null)folderPin.Dispose();if(parentPin!=null)parentPin.Dispose();if(rootPin!=null)rootPin.Dispose();}
  }
 }
}
