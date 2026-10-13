using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // A single SaveAs executor. Separate fresh verification is mandatory.
 internal static class OwnedSaveAs {
  internal interface IContext : DocumentBindings.IContext {void SaveAs(string path);}
  internal const string Qualification="One new owned SaveAs target; saved bytes and observable content only, not full topology. Returned success remains unverified/fenced until separate fresh verification. Unknown outcome is never saved/retried/inverted/closed automatically.";
  internal static void Validate(Dictionary<string,object> parameters) {
   if(parameters.Count!=2||!parameters.ContainsKey("targetId")||!EntitySets.Hex(parameters["targetId"],32)||
      !parameters.ContainsKey("targetSha256")||!EntitySets.Hex(parameters["targetSha256"],64))throw new ArgumentException("Invalid SaveAs target proof identity.");
  }
  static Dictionary<string,object> Clone(Dictionary<string,object> data){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(data));}
  internal static string Content(Dictionary<string,object> data) {
   data=Clone(data);EntitySets.ObservedDigest(data);
   // Preserve strict observed property types and values while removing only the
   // process/document identity changed by a qualified SaveAs/reopen.
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})data.Remove(key);
   string[] fields={"entityCount","entities","levelsContainingEntities","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes",
    "lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"};
   var body=fields.ToDictionary(key=>key,key=>data[key]);body["entities"]=EntitySets.Inventory(data).OrderBy(e=>(int)e["nativeTypeCode"]).ThenBy(e=>(int)e["nativeId"]).ToArray();
   return EntitySets.Canonical(body);
  }
  internal static Dictionary<string,object> SavedProof(string path) {
   OwnedCopies.NoReparse(path);
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
    long length=file.Length;if(length<1||length>1073741824)throw new IOException("Saved result size invalid.");
    var identity=OwnedCopies.FileIdentity(file,path,true);string first=OwnedCopies.Digest(file),second=OwnedCopies.Digest(file);
    if(file.Length!=length||first!=second||EntitySets.Canonical(identity)!=EntitySets.Canonical(OwnedCopies.FileIdentity(file,path,true)))throw new IOException("Saved result bytes/identity changed while reading.");
    return new Dictionary<string,object>{{"path",path},{"sha256",first},{"length",length},{"fileIdentity",identity}};
   }
  }
  static string WindowKey(Dictionary<string,object> window) {
   var value=new Dictionary<string,object>(window);value.Remove("selectedRaw");value.Remove("nativeWindowId");return EntitySets.Canonical(value);
  }
  internal static void CheckAfterWindows(Dictionary<string,object> before,Dictionary<string,object> after,string working,string target) {
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(before[key],after[key]))throw new InvalidOperationException("SaveAs process/session changed.");
   var previous=WorkingCopyOpen.Windows(before);var current=WorkingCopyOpen.Windows(after);
   if(!Equals(before["windowCount"],after["windowCount"])||previous.Length!=current.Length)throw new InvalidOperationException("SaveAs unexpectedly changed window count.");
   var active=after["activeWindow"] as Dictionary<string,object>;
   if(active==null||!active.ContainsKey("modelPath")||!string.Equals(OwnedCopies.LocalPath((string)active["modelPath"]),target,StringComparison.OrdinalIgnoreCase)||!Equals(active["selectedRaw"],1))throw new InvalidOperationException("SaveAs target is not active.");
   var originals=previous.Where(window=>window.ContainsKey("modelPath")&&string.Equals(OwnedCopies.LocalPath((string)window["modelPath"]),working,StringComparison.OrdinalIgnoreCase)).ToArray();
   if(originals.Length!=1)throw new InvalidOperationException("SaveAs source window ambiguous.");
   // SAVEASPATH may assign a new model ID while retaining the COM object and
   // model window. A model ID is checked against post-save observations below.
   foreach(string key in new[]{"windowNumber","nativeWindowId","type"})if(!originals[0].ContainsKey(key)||!active.ContainsKey(key)||!Equals(originals[0][key],active[key]))throw new InvalidOperationException("SaveAs retained window identity changed.");
   if(!active.ContainsKey("modelId")||!(active["modelId"] is int)||(int)active["modelId"]<=0||current.Count(value=>value.ContainsKey("modelId")&&Equals(value["modelId"],active["modelId"]))!=1||
      current.Any(value=>value.ContainsKey("modelPath")&&string.Equals(OwnedCopies.LocalPath((string)value["modelPath"]),working,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("SaveAs current model identity ambiguous.");
   foreach(var window in previous) {
    if(ReferenceEquals(window,originals[0]))continue;
    if(current.Count(value=>WindowKey(window)==WindowKey(value))!=1)throw new InvalidOperationException("Unrelated SaveAs window changed.");
   }
   var targets=current.Where(value=>value.ContainsKey("modelPath")&&string.Equals(OwnedCopies.LocalPath((string)value["modelPath"]),target,StringComparison.OrdinalIgnoreCase)).ToArray();
   if(targets.Length!=1||EntitySets.Canonical(targets[0])!=EntitySets.Canonical(active)||
      !active.ContainsKey("modelName")||!string.Equals(active["modelName"] as string,Path.GetFileNameWithoutExtension(target),StringComparison.OrdinalIgnoreCase)||
      current.Select(window=>window["nativeWindowId"]).Distinct().Count()!=current.Length)throw new InvalidOperationException("SaveAs target window listing/model identity ambiguous.");
  }
  internal static void CheckSavedModel(Dictionary<string,object> model,Dictionary<string,object> listing) {
   var active=listing.ContainsKey("activeWindow")?listing["activeWindow"] as Dictionary<string,object>:null;
   if(model==null||active==null||!model.ContainsKey("id")||!(model["id"] is int)||(int)model["id"]<=0||
      !active.ContainsKey("modelId")||!Equals(model["id"],active["modelId"])||!model.ContainsKey("name")||!active.ContainsKey("modelName")||!Equals(model["name"],active["modelName"])||
      !model.ContainsKey("path")||!active.ContainsKey("modelPath")||!string.Equals(OwnedCopies.LocalPath((string)model["path"]),OwnedCopies.LocalPath((string)active["modelPath"]),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("SaveAs post-save model observation changed.");
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> parameters,IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_save_as_rejected_before_change"},{"operationId",id},{"bridgeVersion",Program.ReleaseVersion},
    {"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"retryAllowed",false},{"qualification",Qualification}};
   NativeJournal journal=null;bool attempted=false,claimed=false;string fence=null,fenceSha=null;Dictionary<string,object> sourceProof=null;
   try {
    Validate(parameters);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid SaveAs operation identity.");
    using(var target=new OwnedSaveTargets.TargetLease(root,(string)parameters["targetId"],(string)parameters["targetSha256"])) {
     string path=(string)target.Plan["resultPath"];WorkingCopyOpen.CommandPath(path);
     var selection=target.Expected["selectionBefore"] as IList;if(selection==null||selection.Count!=0)throw new InvalidOperationException("Empty selection required before SaveAs.");
     var binding=DocumentBindings.Load(root,new Dictionary<string,object>{{"bindingId",target.Plan["bindingId"]},{"bindingSha256",target.Plan["bindingSha256"]}});
     sourceProof=new Dictionary<string,object>{{"requestId",target.Plan["inspectionRequestId"]},{"sha256",target.Plan["inspectionSha256"]}};
     DocumentBindings.Check(target.Copy,target.Expected,target.Expires,context,result,(string)binding["windowContextSha256"]);target.Recheck();
     var windows=context.Windows();
     if(WorkingCopyOpen.Windows(windows).Any(value=>value.ContainsKey("modelName")&&string.Equals(value["modelName"] as string,Path.GetFileNameWithoutExtension(path),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("SaveAs model name already open.");
     var saved=WorkingCopyOpen.SavedHashes(windows);result["beforeWindows"]=windows;result["savedFileHashesBefore"]=saved;
     var plan=new Dictionary<string,object>(parameters){{"schemaVersion",1},{"operationId",id},{"operation","save_owned_document_as"},{"executionEnabled",true},
      {"copyId",target.Plan["copyId"]},{"ownershipSha256",target.Plan["ownershipSha256"]},{"resultPath",path},{"createdUtc",DateTime.UtcNow.ToString("o")}};
     string folder=Path.Combine(root,"saveexecutions");Directory.CreateDirectory(folder);OwnedCopies.NoReparse(folder);
     string planSha=NativeJournal.Freeze(Path.Combine(folder,id+".json"),plan);result["plan"]=plan;result["planSha256"]=planSha;
     result["copyId"]=target.Plan["copyId"];result["ownershipSha256"]=target.Plan["ownershipSha256"];result["resultPath"]=path;result["localArtifactsMayExist"]=true;
     journal=new NativeJournal(root,id,planSha);journal.Append("prepared",new Dictionary<string,object>{{"operation","save_owned_document_as"},{"executionEnabled",true}});
     fence=OwnedTranslation.FencePath(root,(string)target.Plan["copyId"]);
     fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",planSha},{"copyId",target.Plan["copyId"]},{"ownershipSha256",target.Plan["ownershipSha256"]},{"automaticReplayForbidden",true}});
     result["executionFenceSha256"]=fenceSha;
     NativeJournal.Freeze(Path.Combine(OwnedSaveTargets.Folder(root,(string)parameters["targetId"]),"save-once.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",planSha},{"targetId",parameters["targetId"]},{"automaticReplayForbidden",true}});claimed=true;
     journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"copyId",target.Plan["copyId"]},{"targetId",parameters["targetId"]}});
     target.RecheckDependencies();DocumentBindings.Check(target.Copy,target.Expected,target.Expires,context,result,(string)binding["windowContextSha256"]);
     if(File.Exists(path)||Directory.Exists(path))throw new IOException("SaveAs target appeared after claim.");
     NativeJournal.VerifiedJson(fence,fenceSha,65536);
     journal.Append("command_started",new Dictionary<string,object>{{"command","FILE SAVEASPATH / fixed NEW owned target"},{"cadSaveAttempted",true}});
     context.ConfirmDocument();target.RecheckDependencies();
     if(DocumentBindings.WindowDigest(context.Windows())!=(string)binding["windowContextSha256"]||File.Exists(path)||Directory.Exists(path))throw new InvalidOperationException("SaveAs final context/target changed.");
     attempted=true;result["cadSaveAttempted"]=true;result["contextChangeAttempted"]=true;context.SaveAs(path);
     result["saveCommandReturned"]=true;
     journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id},{"saveCommandReturned",true}});
     var file=SavedProof(path);result["savedResult"]=file;
     for(int pass=0;pass<2;pass++) {
      var fresh=Clone(context.Observation());var model=(Dictionary<string,object>)fresh["model"];
      if(!string.Equals(OwnedCopies.LocalPath((string)model["path"]),path,StringComparison.OrdinalIgnoreCase)||Content(fresh)!=Content(target.Expected))throw new InvalidOperationException("SaveAs observable content/units changed.");
      context.ConfirmDocument();result["afterModel"]=model;
     }
     var afterWindows=context.Windows();CheckSavedModel((Dictionary<string,object>)result["afterModel"],afterWindows);
     CheckAfterWindows(windows,afterWindows,target.Copy.WorkingPath,path);target.RecheckDependencies();
     if(saved.Any(pair=>Program.Hash(pair.Key)!=pair.Value)||EntitySets.Canonical(SavedProof(path))!=EntitySets.Canonical(file))throw new IOException("Saved/protected files changed after SaveAs.");
     result["commandCalls"]=1;result["savedFilesUnchanged"]=true;result["copyFenced"]=true;result["state"]="owned_save_as_executed_unverified";
     journal.Append("executed_unverified",new Dictionary<string,object>{{"independentVerificationRequired",true},{"savedResultSha256",file["sha256"]}});
    }
   }catch(Exception error) {
    bool unknown=attempted||journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State);
    result["state"]=unknown?"owned_save_as_outcome_unknown":"owned_save_as_rejected_before_change";result["error"]=error.Message;
    if(journal!=null)try {
     if(new[]{"prepared","execution_claimed"}.Contains(journal.State)) {
      journal.Append("rejected_before_change",new Dictionary<string,object>{{"cadSaveAttempted",false},{"evidence",new[]{sourceProof}}});
      if(fenceSha!=null){NativeJournal.VerifiedJson(fence,fenceSha,65536);File.Delete(fence);}
     }else if(new[]{"command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"cadSaveAttempted",attempted},{"error",error.Message}});
    }catch(Exception journalError){result["journalError"]=journalError.Message;}
    result["intentClaimed"]=claimed;result["copyFenced"]=fence!=null&&File.Exists(fence);
    result["recoveryInstructions"]="Read original journal and independent receipts. Never replay SaveAs, overwrite, inverse, close or remove unknown fence automatically.";
   }
   if(journal!=null)result["recordedJournalState"]=journal.State;result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> parameters,bool verify) {
   var refused=new Dictionary<string,object>{{"state",verify?"owned_save_as_verification_rejected":"owned_save_as_rejected_before_change"},
    {"bridgeVersion",Program.ReleaseVersion},{"cadSaveAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},
    {"powerShapeConnectionAttempted",false},{"retryAllowed",false},{"qualification",Qualification}};
   try {
    Config config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedSavesExperimental)throw new InvalidOperationException("Experimental owned SaveAs execution disabled.");
    if(verify)OwnedSaveVerification.Validate(parameters);else Validate(parameters);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));OwnedCopies.NoReparse(Path.Combine(Program.Root,"operations"));
    using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     refused["powerShapeConnectionAttempted"]=true;
     using(var context=new DocumentBindings.ComContext(config))return verify?OwnedSaveVerification.Verify(Program.Root,parameters,context):Execute(Program.Root,id,parameters,context);
    }
   }catch(Exception error){refused["error"]=error.Message;}
   refused["timestampUtc"]=DateTime.UtcNow.ToString("o");return refused;
  }
 }
}
