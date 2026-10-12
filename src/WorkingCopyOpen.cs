using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace PowerShapeBridge {
 public static class WorkingCopyOpen {
  public const string Qualification="One attempt to open an owned saved Working.psmodel in one new model window. Existing windows and saved bytes are checked; no save, close, entity selection or geometry commands. Journal records executed_unverified pending independent verification; full topology and unsaved state of other documents are not certified.";
  internal interface IContext : IDisposable {
   Dictionary<string,object> Windows();Dictionary<string,object> Observation();void ConfirmDocument();void Open(string path);
  }
  internal sealed class ComContext : IContext {
   object application,document;bool opened,rebound;
   internal ComContext(Config config){try{application=DocumentSession.Attach(config);dynamic ps=application;if(!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("Active model required before open.");document=ps.ActiveDocument;ConfirmDocument();}catch{Dispose();throw;}}
   public Dictionary<string,object> Windows(){return DocumentSession.ReadStable(application);}
   public Dictionary<string,object> Observation(){if(opened&&!rebound){if(Marshal.IsComObject(document))Marshal.ReleaseComObject(document);dynamic ps=application;document=ps.ActiveDocument;rebound=true;}return Probe.ReadInContext(application,document);}
   public void ConfirmDocument(){dynamic ps=application;if(Convert.ToBoolean(ps.Busy())||!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("Active document unavailable/busy.");object current=null;IntPtr retained=IntPtr.Zero,active=IntPtr.Zero;try{current=ps.ActiveDocument;retained=Marshal.GetIUnknownForObject(document);active=Marshal.GetIUnknownForObject(current);if(retained!=active)throw new InvalidOperationException("Active COM document changed.");}finally{if(active!=IntPtr.Zero)Marshal.Release(active);if(retained!=IntPtr.Zero)Marshal.Release(retained);if(current!=null&&Marshal.IsComObject(current))Marshal.ReleaseComObject(current);}}
   public void Open(string path){CheckpointTest.Open((dynamic)application,path);opened=true;}
   public void Dispose(){if(document!=null&&Marshal.IsComObject(document))Marshal.ReleaseComObject(document);document=null;if(application!=null&&Marshal.IsComObject(application))Marshal.ReleaseComObject(application);application=null;}
  }
  public static void ValidateParameters(Dictionary<string,object> args) {
   if(args.Count!=9||new[]{"copyId","inspectionRequestId","listingRequestId"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],32))||new[]{"ownershipSha256","inspectionSha256","listingSha256"}.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],64))||!args.ContainsKey("expiresUtc")||!args.ContainsKey("windowNumber")||!(args["windowNumber"] is int)||(int)args["windowNumber"]<1||(int)args["windowNumber"]>4096||!args.ContainsKey("modelId")||!(args["modelId"] is int)||(int)args["modelId"]<0)throw new ArgumentException("Invalid owned open parameters.");EntitySets.Expiry(args["expiresUtc"]);
  }
  internal static void CommandPath(string path){OwnedCopies.LocalPath(path);if(!Regex.IsMatch(path,@"^[A-Za-z]:[\\/][A-Za-z0-9_\\/.:\-]+$")||path.Substring(2).Contains(':')||Path.GetExtension(path)!=".psmodel")throw new ArgumentException("CAD open supports an ASCII local installation path without spaces, quotes or command delimiters.");}
  internal static Dictionary<string,object>[] Windows(Dictionary<string,object> snapshot){return Program.Json.Deserialize<List<Dictionary<string,object>>>(Program.Json.Serialize(snapshot["windows"])).ToArray();}
  static string WindowKey(Dictionary<string,object> window){var copy=new Dictionary<string,object>(window);copy.Remove("selectedRaw");copy.Remove("nativeWindowId");return EntitySets.Canonical(copy);}
  internal static void CheckBefore(Dictionary<string,object> args,Dictionary<string,object> listing,Dictionary<string,object> expected,IContext context,string working) {
   var expiry=EntitySets.Expiry(args["expiresUtc"]);var issued=EntitySets.Expiry(listing["timestampUtc"]);
   if(!Equals(listing["state"],"documents_listed_verified")||expiry>issued.AddSeconds(300)||expiry<issued||issued>DateTime.UtcNow.AddSeconds(5))throw new InvalidOperationException("Invalid listing lease.");
   if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Window context changed before open.");
   if(Windows(listing).Any(w=>w.ContainsKey("modelName")&&string.Equals(w["modelName"] as string,Path.GetFileNameWithoutExtension(working),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Model name already open; no open command issued. Use a newly owned copy with a unique name.");
   var active=listing["activeWindow"] as Dictionary<string,object>;var expectedData=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(expected));var model=(Dictionary<string,object>)expectedData["model"];
   if(active==null||!active.ContainsKey("modelId")||!Equals(active["selectedRaw"],1)||!Equals(active["windowNumber"],args["windowNumber"])||!Equals(active["modelId"],args["modelId"])||!Equals(active["modelId"],model["id"])||!Equals(active["modelName"],model["name"])||!Equals(active["modelPath"],model["path"])||Windows(listing).Count(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],model["id"]))!=1)throw new InvalidOperationException("Listing choice is not the inspected active model window.");
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(listing[key],expectedData[key]))throw new InvalidOperationException("Listing/inspection process identity differs.");
   if(Windows(listing).Any(w=>w.ContainsKey("modelPath")&&!string.IsNullOrEmpty((string)w["modelPath"])&&string.Equals(OwnedCopies.LocalPath((string)w["modelPath"]),working,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Owned copy already open; no activation or replay.");
   var selection=expectedData["selectionBefore"] as IList;if(selection==null||selection.Count!=0)throw new InvalidOperationException("Empty entity selection required before owned open.");
   EntitySets.CheckObserved(expected,context.Observation(),new int[0][],expiry,DateTime.UtcNow);EntitySets.CheckObserved(expected,context.Observation(),new int[0][],expiry,DateTime.UtcNow);context.ConfirmDocument();
   if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Window context changed during open guard.");
  }
  internal static Dictionary<string,object> CheckAfter(Dictionary<string,object> before,Dictionary<string,object> after,string working) {
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})if(!Equals(before[key],after[key]))throw new InvalidOperationException("Process identity changed during open.");
   var previous=Windows(before);var current=Windows(after);if((int)after["windowCount"]!=(int)before["windowCount"]+1||current.Length!=previous.Length+1)throw new InvalidOperationException("Expected exactly one new model window.");
   foreach(var old in previous)if(current.Count(w=>WindowKey(w)==WindowKey(old))!=1)throw new InvalidOperationException("An existing window/document was replaced or changed.");
   var added=current.Where(w=>!previous.Any(old=>WindowKey(old)==WindowKey(w))).ToArray();var active=after["activeWindow"] as Dictionary<string,object>;
   if(added.Length!=1||active==null||!added[0].ContainsKey("modelPath")||!string.Equals(OwnedCopies.LocalPath((string)added[0]["modelPath"]),working,StringComparison.OrdinalIgnoreCase)||!Equals(added[0]["selectedRaw"],1)||WindowKey(active)!=WindowKey(added[0])||current.Select(w=>w["nativeWindowId"]).Distinct().Count()!=current.Length||previous.Any(w=>Equals(w["windowNumber"],added[0]["windowNumber"])||w.ContainsKey("modelId")&&Equals(w["modelId"],added[0]["modelId"])))throw new InvalidOperationException("New active owned model identity was not established.");
   return added[0];
  }
  internal static Dictionary<string,string> SavedHashes(Dictionary<string,object> listing) {
   var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var window in Windows(listing).Where(w=>w.ContainsKey("modelPath")&&!string.IsNullOrWhiteSpace((string)w["modelPath"]))) {
    string path=OwnedCopies.LocalPath((string)window["modelPath"]);if(!File.Exists(path))throw new IOException("Listed saved document file missing.");OwnedCopies.NoReparse(path);
    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(file.Length>1073741824)throw new IOException("Saved document hash limit.");using(var hash=SHA256.Create())result[path]=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
   }return result;
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,Dictionary<string,object> listing,Dictionary<string,object> expected,IContext context,Dictionary<string,object> savedReopen=null,Action sourceCheck=null,Action<Dictionary<string,object>> contentCheck=null) {
   var result=new Dictionary<string,object>{{"state","working_copy_open_rejected"},{"operationId",id},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"retryAllowed",false},{"qualification",Qualification}};
   NativeJournal journal=null;bool attempted=false;
   try {
    ValidateParameters(args);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid open request identity.");
    using(var lease=OwnedCopies.Hold(root,(string)args["copyId"],(string)args["ownershipSha256"])) {
     string claim=Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"open-once.json");if(File.Exists(claim))throw new InvalidOperationException("Owned copy has an earlier open claim; inspect journal, never replay.");CommandPath(lease.WorkingPath);
     if(sourceCheck!=null)sourceCheck();CheckBefore(args,listing,expected,context,lease.WorkingPath);lease.Recheck();var saved=SavedHashes(listing);result["before"]=listing;result["savedFileHashesBefore"]=saved;result["copyId"]=args["copyId"];result["ownershipSha256"]=args["ownershipSha256"];result["workingPath"]=lease.WorkingPath;
     string planPath=Path.Combine(root,"copyopenplans",id+".json");Directory.CreateDirectory(Path.GetDirectoryName(planPath));var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"operationId",id},{"operation",savedReopen==null?"open_working_copy":"open_owned_saved_result"},{"bridgeVersion",Program.ReleaseVersion},{"createdUtc",DateTime.UtcNow.ToString("o")},{"geometryExecutionEnabled",false},{"workingPath",lease.WorkingPath}};
     if(savedReopen!=null){OwnedSavedReopenLease.Validate(savedReopen);foreach(var field in savedReopen)plan.Add(field.Key,field.Value);}
     string sha=NativeJournal.Freeze(planPath,plan);result["planSha256"]=sha;result["plan"]=plan;
     journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"operation",plan["operation"]},{"contextChangeAttempted",false}});
     NativeJournal.Freeze(claim,new{operationId=id,planSha256=sha,copyId=args["copyId"],ownershipSha256=args["ownershipSha256"],automaticReplayForbidden=true});journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"copyId",args["copyId"]}});
     CheckBefore(args,listing,expected,context,lease.WorkingPath);lease.Recheck();journal.Append("command_started",new Dictionary<string,object>{{"commandSequence","FILE OPENPATH / fixed PATHNAME / ACCEPT"},{"contextChangeAttempted",true}});
     context.ConfirmDocument();if(sourceCheck!=null)sourceCheck();if(DateTime.UtcNow>=EntitySets.Expiry(args["expiresUtc"])||DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(listing))throw new InvalidOperationException("Context/expiry changed immediately before open.");
     attempted=true;result["contextChangeAttempted"]=true;context.Open(lease.WorkingPath);journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id}});
     var after=context.Windows();var target=CheckAfter(listing,after,lease.WorkingPath);var observation=context.Observation();EntitySets.Inventory(observation);var data=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(observation));var model=(Dictionary<string,object>)data["model"];
     if(!Equals(model["id"],target["modelId"])||!Equals(model["name"],target["modelName"])||!string.Equals(OwnedCopies.LocalPath((string)model["path"]),lease.WorkingPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Opened model read differs from new window.");
     if(contentCheck!=null)contentCheck(observation);if(sourceCheck!=null)sourceCheck();lease.Recheck();var afterHashes=SavedHashes(listing);if(saved.Count!=afterHashes.Count||saved.Any(x=>!afterHashes.ContainsKey(x.Key)||afterHashes[x.Key]!=x.Value))throw new IOException("An existing saved file changed during open.");context.ConfirmDocument();if(DocumentBindings.WindowDigest(context.Windows())!=DocumentBindings.WindowDigest(after))throw new InvalidOperationException("Window context changed during postcondition read.");
     result["after"]=after;result["openedWindow"]=target;result["inspection"]=observation;result["savedFilesUnchanged"]=true;result["workingSha256"]=lease.Inspection["workingSha256"];result["checkpointVerified"]=true;result["state"]="working_copy_opened_observed";
     journal.Append("executed_unverified",new Dictionary<string,object>{{"ownedModelId",target["modelId"]},{"sameWorkerPostconditionsPassed",true},{"independentVerificationRequired",true}});result["recordedJournalState"]=journal.State;return result;
    }
   }catch(Exception error) {
    result["state"]=attempted?"working_copy_open_outcome_unknown":"working_copy_open_rejected";result["error"]=error.Message;result["localArtifactsMayExist"]=journal!=null;result["recoveryInstructions"]="Read operation_result, list_documents, inspect_active_model and copy files; never automatically repeat open or close/save documents.";
    if(journal!=null)try {
     if(journal.State=="prepared"||journal.State=="execution_claimed") {
      var evidence=new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}};
      journal.Append("rejected_before_change",new Dictionary<string,object>{{"contextChangeAttempted",false},{"evidence",new[]{evidence}}});
     }else if(new[]{"command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"contextChangeAttempted",attempted},{"error",error.Message}});
     result["recordedJournalState"]=journal.State;
    }catch(Exception failedJournal){result["journalError"]=failedJournal.Message;}
    return result;
   }
  }
  public static Dictionary<string,object> Run(string requestId,Dictionary<string,object> args) {
   try {
    var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.workingCopyOpenExperimental)throw new InvalidOperationException("Experimental owned open disabled.");ValidateParameters(args);
    var expected=EntitySets.Receipt((string)args["inspectionRequestId"],(string)args["inspectionSha256"]);EntitySets.CheckObserved(expected,expected,new int[0][],EntitySets.Expiry(args["expiresUtc"]),DateTime.UtcNow);var listing=EntitySets.Receipt((string)args["listingRequestId"],(string)args["listingSha256"]);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var context=new ComContext(config))return Execute(Program.Root,requestId,args,listing,expected,context);
   }catch(Exception error){return new Dictionary<string,object>{{"state","working_copy_open_rejected"},{"operationId",requestId},{"bridgeVersion",Program.ReleaseVersion},{"error",error.Message},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false}};}
  }
  internal sealed class FakeContext : IContext {
   internal Dictionary<string,object> Listing,Snapshot;internal string Working;internal int Commands;internal bool FailOpen,ChangedWindow;
   public Dictionary<string,object> Windows(){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(Listing));}
   public Dictionary<string,object> Observation(){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(Snapshot));}
   public void ConfirmDocument(){}
   public void Open(string path){Commands++;if(FailOpen)throw new InvalidOperationException("Synthetic interrupted open.");var old=WorkingCopyOpen.Windows(Listing);foreach(var w in old)w["selectedRaw"]=0;var added=new Dictionary<string,object>{{"windowNumber",2},{"nativeWindowId",8},{"type","Model"},{"selectedRaw",1},{"modelId",10},{"modelName","Working"},{"modelPath",Working}};Listing["windows"]=old.Concat(new[]{added}).ToArray();Listing["activeWindow"]=added;Listing["windowCount"]=2;Snapshot["model"]=new Dictionary<string,object>{{"id",10},{"name","Working"},{"path",Working}};if(ChangedWindow)old[0]["modelName"]="Changed";}
   public void Dispose(){}
  }
  public static int SelfTest() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Owned open test failed at "+count);count++;};Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(IOException){rejected=true;}check(rejected);};
   string root=Path.Combine(Program.Root,"work","open-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string source=Path.Combine(root,"Source.psmodel");File.WriteAllText(source,"Opaque synthetic file, never loaded by PowerShape.");string copyId=Guid.NewGuid().ToString("N");var copy=OwnedCopies.Create(root,copyId,source,Program.Hash(source));string working=(string)((Dictionary<string,object>)((Dictionary<string,object>)copy["ownership"])["working"])["path"];
   var snapshot=new Dictionary<string,object>();foreach(string key in new[]{"levelsContainingEntities","selectionBefore","selectionAfter","workplanes","entities"})snapshot[key]=new object[0];foreach(string key in new[]{"lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})snapshot[key]=null;snapshot["state"]="succeeded_verified";snapshot["modelScope"]="active_document_read_only";snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");snapshot["processId"]=1;snapshot["processStartedUtc"]="2026-10-09T10:00:00Z";snapshot["windowsSessionId"]=1;snapshot["productVersion"]="23.2.2";snapshot["model"]=new Dictionary<string,object>{{"id",9},{"name","Source"},{"path",source}};snapshot["entityCount"]=0;snapshot["contextBefore"]=new[]{0,0};snapshot["contextAfter"]=new[]{0,0};
   var window=new Dictionary<string,object>{{"windowNumber",1},{"nativeWindowId",3},{"type","Model"},{"selectedRaw",1},{"modelId",9},{"modelName","Source"},{"modelPath",source}};var listing=new Dictionary<string,object>{{"state","documents_listed_verified"},{"timestampUtc",snapshot["timestampUtc"]},{"processId",1},{"processStartedUtc",snapshot["processStartedUtc"]},{"windowsSessionId",1},{"windowCount",1},{"windows",new[]{window}},{"activeWindow",window}};
   Func<Dictionary<string,object>,Dictionary<string,object>> clone=v=>Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));Func<FakeContext> context=()=>new FakeContext{Snapshot=clone(snapshot),Listing=clone(listing),Working=working};
   var args=new Dictionary<string,object>{{"copyId",copyId},{"ownershipSha256",copy["ownershipSha256"]},{"inspectionRequestId",new string('a',32)},{"inspectionSha256",new string('b',64)},{"listingRequestId",new string('c',32)},{"listingSha256",new string('d',64)},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}};
   var fake=context();string id=Guid.NewGuid().ToString("N");var result=Execute(root,id,args,listing,snapshot,fake);check(Equals(result["state"],"working_copy_opened_observed")&&fake.Commands==1&&Equals(result["recordedJournalState"],"executed_unverified"));check(File.Exists(Path.Combine(root,"operations",id,"000004.json"))&&!File.Exists(Path.Combine(root,"operations",id,"writer.lock")));var replay=context();check(Equals(Execute(root,Guid.NewGuid().ToString("N"),args,listing,snapshot,replay)["state"],"working_copy_open_rejected")&&replay.Commands==0);
   string collisionId=Guid.NewGuid().ToString("N");var collisionCopy=OwnedCopies.Create(root,collisionId,source,Program.Hash(source));string collisionPath=(string)((Dictionary<string,object>)((Dictionary<string,object>)collisionCopy["ownership"])["working"])["path"];
   var collisionListing=clone(listing);var otherWindow=new Dictionary<string,object>(window);otherWindow["windowNumber"]=2;otherWindow["nativeWindowId"]=8;otherWindow["modelId"]=10;otherWindow["selectedRaw"]=0;otherWindow["modelName"]=Path.GetFileNameWithoutExtension(collisionPath).ToUpperInvariant();otherWindow["modelPath"]=Path.Combine(root,"Elsewhere.psmodel");collisionListing["windows"]=new[]{window,otherWindow};collisionListing["windowCount"]=2;
   var collisionContext=context();collisionContext.Listing=clone(collisionListing);var collisionArgs=new Dictionary<string,object>(args);collisionArgs["copyId"]=collisionId;collisionArgs["ownershipSha256"]=collisionCopy["ownershipSha256"];string refusedId=Guid.NewGuid().ToString("N");var refused=Execute(root,refusedId,collisionArgs,collisionListing,snapshot,collisionContext);
   check(Equals(refused["state"],"working_copy_open_rejected")&&collisionContext.Commands==0&&!Equals(refused["contextChangeAttempted"],true));
   check(!Directory.Exists(Path.Combine(root,"operations",refusedId))&&!File.Exists(Path.Combine(Path.GetDirectoryName(collisionPath),"open-once.json")));
   foreach(string path in new[]{"D:/with space/Working.psmodel","D:/q'quote/Working.psmodel","D:/x;ACCEPT/Working.psmodel","D:/Работа/Working.psmodel"})rejects(()=>CommandPath(path));CommandPath(working);count++;
   var extra=new Dictionary<string,object>(args){{"command","SAVE"}};rejects(()=>ValidateParameters(extra));var wrong=context();wrong.Listing["processId"]=2;rejects(()=>CheckBefore(args,listing,snapshot,wrong,working));var foreign=new Dictionary<string,object>(args){{"unused",0}};foreign.Remove("unused");foreign["modelId"]=10;rejects(()=>CheckBefore(foreign,listing,snapshot,context(),working));var expired=new Dictionary<string,object>(args);expired["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");rejects(()=>CheckBefore(expired,listing,snapshot,context(),working));
   var changed=context();changed.ChangedWindow=true;changed.Open(working);rejects(()=>CheckAfter(listing,changed.Listing,working));var noNew=context();rejects(()=>CheckAfter(listing,noNew.Listing,working));
   var renumbered=context();renumbered.Open(working);var renumberedWindows=Windows(renumbered.Listing);renumberedWindows[0]["nativeWindowId"]=8;renumberedWindows[1]["nativeWindowId"]=3;renumbered.Listing["windows"]=renumberedWindows;renumbered.Listing["activeWindow"]=renumberedWindows[1];check(Equals(CheckAfter(listing,renumbered.Listing,working)["modelId"],10));
   var duplicate=clone(renumbered.Listing);var duplicateWindows=Windows(duplicate);duplicateWindows[0]["nativeWindowId"]=3;duplicate["windows"]=duplicateWindows;rejects(()=>CheckAfter(listing,duplicate,working));
   var replaced=clone(renumbered.Listing);var replacedWindows=Windows(replaced);replacedWindows[0]["modelId"]=11;replaced["windows"]=replacedWindows;rejects(()=>CheckAfter(listing,replaced,working));
   string second=Guid.NewGuid().ToString("N");var secondCopy=OwnedCopies.Create(root,second,source,Program.Hash(source));var failureArgs=new Dictionary<string,object>(args);failureArgs["copyId"]=second;failureArgs["ownershipSha256"]=secondCopy["ownershipSha256"];var failure=context();failure.Working=(string)((Dictionary<string,object>)((Dictionary<string,object>)secondCopy["ownership"])["working"])["path"];failure.FailOpen=true;string failedId=Guid.NewGuid().ToString("N");check(Equals(Execute(root,failedId,failureArgs,listing,snapshot,failure)["state"],"working_copy_open_outcome_unknown")&&failure.Commands==1);var retry=context();check(Equals(Execute(root,Guid.NewGuid().ToString("N"),failureArgs,listing,snapshot,retry)["state"],"working_copy_open_rejected")&&retry.Commands==0);
   rejects(()=>NativeJournal.Transition("executed_unverified","execution_claimed",new Dictionary<string,object>()));rejects(()=>NativeJournal.Transition("command_returned","succeeded_verified",new Dictionary<string,object>()));
   var resumed=NativeJournal.ForVerification(root,id,(string)result["planSha256"]);check(resumed.State=="executed_unverified"&&resumed.CommandReturnedUtc>DateTime.MinValue);
   var evidenceEntry=new Dictionary<string,object>{{"requestId",new string('a',32)},{"sha256",new string('b',64)}};var evidence=new Dictionary<string,object>{{"evidence",new[]{evidenceEntry}}};resumed.Append("succeeded_verified",evidence);check(NativeJournal.ForVerification(root,id,(string)result["planSha256"]).State=="succeeded_verified");rejects(()=>resumed.Append("execution_claimed",new Dictionary<string,object>()));
   rejects(()=>NativeJournal.ForVerification(root,failedId,(string)ExecuteResultPlan(root,failedId)));rejects(()=>NativeJournal.ForVerification(root,id,new string('0',64)));
   string marker=Path.Combine(root,"operations",id,"writer.lock");File.WriteAllText(marker,"synthetic interrupted writer");rejects(()=>NativeJournal.ForVerification(root,id,(string)result["planSha256"]));File.Delete(marker);
   string tail=Path.Combine(root,"operations",id,"000005.json");byte[] intact=File.ReadAllBytes(tail);var tampered=Program.Json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(intact));tampered["previousSha256"]=new string('0',64);File.WriteAllText(tail,Program.Json.Serialize(tampered));rejects(()=>NativeJournal.ForVerification(root,id,(string)result["planSha256"]));File.WriteAllBytes(tail,intact);
   var verification=new Dictionary<string,object>(args);verification.Remove("copyId");verification.Remove("ownershipSha256");verification["openRequestId"]=id;verification["openReceiptSha256"]=new string('b',64);OwnedOpenVerification.ValidateParameters(verification);count++;verification["command"]="FILE OPENPATH";rejects(()=>OwnedOpenVerification.ValidateParameters(verification));
   if(Program.ReadConfig().workingCopyOpenExperimental){FileRequests.Parse(new string('e',32),Program.Json.Serialize(new{requestId=new string('e',32),operation="recipe.prepare",arguments=new{recipeId="open_working_copy",parameters=args}}));count++;rejects(()=>FileRequests.Parse(new string('e',32),Program.Json.Serialize(new{requestId=new string('e',32),operation="recipe.prepare",arguments=new{recipeId="open_working_copy",parameters=extra}})));}
   return count;
  }
  static object ExecuteResultPlan(string root,string id){return Program.Hash(Path.Combine(root,"copyopenplans",id+".json"));}
 }
}
