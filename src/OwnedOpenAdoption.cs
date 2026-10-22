using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Current-document observation recovery. Never resumes or rewrites the OPEN journal.
 internal static class OwnedOpenAdoption {
  internal const string Scope="current_owned_document_after_interrupted_open_v1";
  internal const string Recipe="adopt_owned_open_document";
  internal const string RefreshRecipe="restore_owned_open_adoption";
  static void Need(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
  internal static void Validate(Dictionary<string,object> args,bool refresh=false){
   Need(args.Count==(refresh?12:10)&&args.ContainsKey("openPlanSha256")&&EntitySets.Hex(args["openPlanSha256"],64),"Exact interrupted OPEN plan reference required.");
   var standard=new Dictionary<string,object>(args);standard.Remove("openPlanSha256");
   if(refresh){Need(args.ContainsKey("initialAdoptionId")&&EntitySets.Hex(args["initialAdoptionId"],32)&&args.ContainsKey("initialAdoptionSha256")&&EntitySets.Hex(args["initialAdoptionSha256"],64),"Initial qualified adoption reference required.");standard.Remove("initialAdoptionId");standard.Remove("initialAdoptionSha256");}
   OwnedOpenVerification.ValidateParameters(standard);
  }
  static string Fixed(string root,string folder,string id){Need(EntitySets.Hex(id,32),"Fixed OPEN identity required.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  internal sealed class Interrupted {
   internal readonly Dictionary<string,object> Plan,Before,Receipt,Claim;
   internal readonly Dictionary<string,object>[] Events;
   internal readonly string PlanSha,ReceiptSha,ClaimSha;
   internal readonly string[] JournalHashes;
   internal readonly DateTime ResponseWrittenUtc;
   internal Interrupted(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs,Func<int,bool> processExists){
    string id=(string)args["openRequestId"];PlanSha=(string)args["openPlanSha256"];ReceiptSha=(string)args["openReceiptSha256"];
    string response=Fixed(root,"responses",id);Receipt=proofs.Json(response,ReceiptSha);ResponseWrittenUtc=File.GetLastWriteTimeUtc(response);
    Need(Equals(Receipt["state"],"timeout_outcome_unknown")&&Equals(Receipt["operation"],"--mcp-operation "+id)&&Receipt["workerPid"] is int&&(int)Receipt["workerPid"]>0&&Receipt["workerTimeoutSeconds"] is int&&(int)Receipt["workerTimeoutSeconds"]>=1&&(int)Receipt["workerTimeoutSeconds"]<=110&&Receipt["error"] is Dictionary<string,object>&&Equals(((Dictionary<string,object>)Receipt["error"])["code"],"worker_timeout"),"Only an exact terminal OPEN worker timeout is supported.");
    Need(!processExists((int)Receipt["workerPid"]),"Original worker PID still exists; no adoption.");
    string request=Path.Combine(root,"requests",id+".json.processing.done");
    Need(!File.Exists(Path.Combine(root,"requests",id+".json"))&&!File.Exists(Path.Combine(root,"requests",id+".json.processing")),"Original OPEN is still queued.");
    var envelope=proofs.Json(request,null);Need(envelope.Count==3&&Equals(envelope["requestId"],id)&&Equals(envelope["operation"],"recipe.prepare"),"Terminal request identity differs.");
    var wire=(Dictionary<string,object>)envelope["arguments"];Need(wire.Count==2&&Equals(wire["recipeId"],"open_working_copy"),"Terminal request is not an owned OPEN.");
    var parameters=(Dictionary<string,object>)wire["parameters"];WorkingCopyOpen.ValidateParameters(parameters);
    Plan=proofs.Json(Fixed(root,"copyopenplans",id),PlanSha);
    Need(Plan.Count==16&&Equals(Plan["schemaVersion"],1)&&Equals(Plan["operationId"],id)&&Equals(Plan["operation"],"open_working_copy")&&Equals(Plan["geometryExecutionEnabled"],false),"Unsupported frozen OPEN plan.");
    foreach(var p in parameters)Need(Plan.ContainsKey(p.Key)&&EntitySets.Canonical(Plan[p.Key])==EntitySets.Canonical(p.Value),"Frozen OPEN parameters differ from terminal request.");
    Before=proofs.Json(Fixed(root,"responses",(string)Plan["listingRequestId"]),(string)Plan["listingSha256"],16000000);Need(Equals(Before["state"],"documents_listed_verified"),"Original listing missing.");
    var oldInspection=proofs.Json(Fixed(root,"responses",(string)Plan["inspectionRequestId"]),(string)Plan["inspectionSha256"],16000000);
    var oldActive=(Dictionary<string,object>)Before["activeWindow"];var oldModel=(Dictionary<string,object>)oldInspection["model"];
    Need(Equals(oldActive["modelId"],Plan["modelId"])&&Equals(oldActive["windowNumber"],Plan["windowNumber"])&&Equals(oldModel["id"],oldActive["modelId"])&&Equals(oldModel["path"],oldActive["modelPath"]),"Original inspected/listed context differs.");
    foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(Equals(oldInspection[key],Before[key]),"Original process context differs.");
    string journal=Path.Combine(root,"operations",id);OwnedCopies.NoReparse(journal);
    Need(!File.Exists(Path.Combine(journal,"writer.lock"))&&Directory.GetFiles(journal).Length==3,"Only the complete interrupted three-event OPEN journal is supported.");
    Events=new Dictionary<string,object>[3];JournalHashes=new string[3];string prior=null;DateTime previous=DateTime.MinValue;
    string[] states={"prepared","execution_claimed","command_started"};
    for(int i=0;i<3;i++){
     string path=Path.Combine(journal,i.ToString("D6")+".json");var e=proofs.Json(path,null);Events[i]=e;JournalHashes[i]=Program.Hash(path);
     Need(e.Count==8&&Equals(e["schemaVersion"],1)&&Equals(e["operationId"],id)&&Equals(e["planSha256"],PlanSha)&&Equals(e["sequence"],i)&&Equals(e["previousSha256"],prior)&&Equals(e["state"],states[i]),"Interrupted journal chain differs.");
     var time=EntitySets.Expiry(e["timestampUtc"]);Need(time>=previous&&time<=ResponseWrittenUtc,"Interrupted journal time differs.");previous=time;prior=JournalHashes[i];
    }
    Need(EntitySets.Expiry(Plan["createdUtc"])<=EntitySets.Expiry(Events[0]["timestampUtc"])&&EntitySets.Expiry(Before["timestampUtc"])<=EntitySets.Expiry(Plan["createdUtc"]),"OPEN observation/plan order differs.");
    Need(EntitySets.Canonical(Events[0]["details"])==EntitySets.Canonical(new Dictionary<string,object>{{"operation","open_working_copy"},{"contextChangeAttempted",false}}),"Prepared OPEN details differ.");
    Need(EntitySets.Canonical(Events[1]["details"])==EntitySets.Canonical(new Dictionary<string,object>{{"nativeRequestId",id},{"copyId",Plan["copyId"]}}),"OPEN claim event differs.");
    Need(EntitySets.Canonical(Events[2]["details"])==EntitySets.Canonical(new Dictionary<string,object>{{"commandSequence","FILE OPENPATH / fixed PATHNAME / ACCEPT"},{"contextChangeAttempted",true}}),"Started OPEN details differ.");
    string claim=Path.Combine(Path.GetDirectoryName((string)Plan["workingPath"]),"open-once.json");Claim=proofs.Json(claim,null);ClaimSha=Program.Hash(claim);
    Need(Claim.Count==5&&Equals(Claim["operationId"],id)&&Equals(Claim["planSha256"],PlanSha)&&Equals(Claim["copyId"],Plan["copyId"])&&Equals(Claim["ownershipSha256"],Plan["ownershipSha256"])&&Equals(Claim["automaticReplayForbidden"],true),"Original OPEN once-claim differs.");
   }
  }
  static bool ProcessExists(int pid){try{using(var process=Process.GetProcessById(pid))return !process.HasExited;}catch(ArgumentException){return false;}}
  static FileStream HoldSource(OwnedCopies.OwnershipLease lease){
   var manifest=(Dictionary<string,object>)lease.Inspection["ownership"];var record=(Dictionary<string,object>)manifest["source"];string path=(string)record["path"];OwnedCopies.NoReparse(path);var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   try{Need(source.Length==Convert.ToInt64(record["length"])&&OwnedCopies.Digest(source)==(string)record["sha256"]&&EntitySets.Canonical(OwnedCopies.FileIdentity(source,path,false))==EntitySets.Canonical(record["fileIdentity"]),"Protected source bytes/identity changed.");return source;}catch{source.Dispose();throw;}
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args,bool refresh=false){
   var result=Base("owned_open_adoption_rejected");
   try{var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.ownedOpenAdoptionExperimental,"Owned OPEN adoption disabled.");return Adopt(Program.Root,id,args,null,ProcessExists,refresh);}
   catch(Exception e){result["error"]=e.Message;return result;}
  }
  static Dictionary<string,object> Base(string state){return new Dictionary<string,object>{{"state",state},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"qualificationScope",Scope},{"cadMutationAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false},{"originalCommandReturned",false},{"originalJournalModified",false},{"historicalSavedBytesCertified",false},{"fullTopologyEquivalent",false}};}
  internal static Dictionary<string,object> Adopt(string root,string id,Dictionary<string,object> args,Func<DocumentBindings.IContext> factory,Func<int,bool> processExists,bool refresh=false){
   Validate(args,refresh);Need(EntitySets.Hex(id,32),"Fixed adoption request required.");var result=Base("owned_open_adoption_rejected");
   Directory.CreateDirectory(Path.Combine(root,"operations"));
   using(var guard=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()){
    var original=new Interrupted(root,args,proofs,processExists);string copy=(string)original.Plan["copyId"];string owner=(string)original.Plan["ownershipSha256"];
    string report=Fixed(root,"openadoptions",id),once=Path.Combine(root,"openadoptions",(string)args["openRequestId"]+".once.json");
    Need(!File.Exists(report)&&!Directory.Exists(report),"Observation request already recorded; do not repeat.");
    if(!refresh)Need(!File.Exists(once)&&!Directory.Exists(once),"Adoption already claimed; use separate reconnect validation, never repeat adoption.");
    else {
     var initial=proofs.Json(Fixed(root,"openadoptions",(string)args["initialAdoptionId"]),(string)args["initialAdoptionSha256"],16000000);var claimed=proofs.Json(once,null);
     Need(Equals(initial["state"],"owned_open_document_adopted_observed")&&Equals(initial["nativeProviderQualified"],true)&&Equals(initial["qualificationScope"],Scope)&&Equals(initial["adoptionId"],args["initialAdoptionId"])&&Equals(initial["copyId"],copy)&&Equals(initial["ownershipSha256"],owner),"Only a qualified initial adoption can restore access.");
     var firstArgs=(Dictionary<string,object>)initial["parameters"];foreach(string k in new[]{"openRequestId","openReceiptSha256","openPlanSha256"})Need(Equals(firstArgs[k],args[k])&&Equals(claimed[k],args[k]),"Initial adoption OPEN provenance differs.");
     Need(claimed.Count==10&&Equals(claimed["schemaVersion"],1)&&Equals(claimed["qualificationScope"],Scope)&&Equals(claimed["adoptionId"],args["initialAdoptionId"])&&Equals(claimed["adoptionSha256"],args["initialAdoptionSha256"])&&Equals(claimed["copyId"],copy)&&Equals(claimed["ownershipSha256"],owner)&&Equals(claimed["noCommandReplay"],true),"Initial adoption once record differs.");
    }
    string folder=Path.Combine(root,"workingcopies",copy);
    foreach(string name in new[]{"mutation-fence.json","geometry-export-fence.json"})Need(!File.Exists(Path.Combine(folder,name))&&!Directory.Exists(Path.Combine(folder,name)),"Fenced copies cannot be adopted.");
    var inspection=proofs.Json(Fixed(root,"responses",(string)args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);
    var listing=proofs.Json(Fixed(root,"responses",(string)args["listingRequestId"]),(string)args["listingSha256"],16000000);
    var expiry=EntitySets.Expiry(args["expiresUtc"]);var issued=EntitySets.Expiry(listing["timestampUtc"]);
    Need(Equals(listing["state"],"documents_listed_verified")&&issued>original.ResponseWrittenUtc&&EntitySets.Expiry(inspection["timestampUtc"])>original.ResponseWrittenUtc&&issued<=DateTime.UtcNow.AddSeconds(5)&&expiry>issued&&expiry<=issued.AddSeconds(300),"Fresh independent post-timeout observations required.");
    var active=(Dictionary<string,object>)listing["activeWindow"];Need(Equals(active["modelId"],args["modelId"])&&Equals(active["windowNumber"],args["windowNumber"]),"Explicit adopted choice is not active.");
    foreach(string key in new[]{"selectionBefore","selectionAfter"})Need(inspection[key] is IList&&((IList)inspection[key]).Count==0,"Empty selection required for adoption.");
    foreach(string key in new[]{"contextBefore","contextAfter"})Need(EntitySets.Canonical(inspection[key])==EntitySets.Canonical(new[]{0,0}),"WORLD required for adoption.");
    using(var lease=OwnedCopies.Hold(root,copy,owner))using(var pin=new OwnedWorkingBytePin(lease.WorkingPath))using(var source=HoldSource(lease))using(var context=factory==null?new DocumentBindings.ComContext(Program.ReadConfig()):factory()){
     Need(string.Equals(lease.WorkingPath,(string)original.Plan["workingPath"],StringComparison.OrdinalIgnoreCase),"Original OPEN target is not this owned copy.");
     Need(Equals(lease.Inspection["workingFileChanged"],false),"Initial saved owned bytes changed; outside adoption scope.");
     var target=WorkingCopyOpen.CheckAfter(original.Before,listing,lease.WorkingPath);
     var currentHashes=WorkingCopyOpen.SavedHashes(listing);
     DocumentBindings.Check(lease,inspection,expiry,context,result,DocumentBindings.WindowDigest(listing));
     var after=context.Windows();Need(DocumentBindings.WindowDigest(after)==DocumentBindings.WindowDigest(listing),"Adoption window context changed.");
     var hashesAfter=WorkingCopyOpen.SavedHashes(after);Need(currentHashes.Count==hashesAfter.Count&&currentHashes.All(p=>hashesAfter.ContainsKey(p.Key)&&hashesAfter[p.Key]==p.Value),"Saved bytes changed during adoption observation.");
     lease.Recheck();context.ConfirmDocument();Need(!processExists((int)original.Receipt["workerPid"])&&DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==DocumentBindings.WindowDigest(listing),"Adoption final worker/context/expiry check failed.");
     foreach(string name in new[]{"mutation-fence.json","geometry-export-fence.json"})Need(!File.Exists(Path.Combine(folder,name))&&!Directory.Exists(Path.Combine(folder,name)),"Fence appeared during adoption.");
     bool native=context is DocumentBindings.ComContext;
     var frozen=new Dictionary<string,object>(result){{"schemaVersion",1},{"adoptionId",id},{"parameters",args},{"copyId",copy},{"ownershipSha256",owner},{"workingPath",lease.WorkingPath},{"openedWindow",target},{"currentListing",after},{"originalJournalHashes",original.JournalHashes},{"originalClaimSha256",original.ClaimSha},{"currentSavedFileHashes",currentHashes},{"nativeProviderQualified",native},{"expiresUtc",args["expiresUtc"]}};
     frozen["state"]=native?(refresh?"owned_open_adoption_refreshed_observed":"owned_open_document_adopted_observed"):"owned_open_document_observed_unqualified";frozen["timestampUtc"]=DateTime.UtcNow.ToString("o");
     Directory.CreateDirectory(Path.GetDirectoryName(report));string sha=NativeJournal.Freeze(report,frozen);
     if(native&&!refresh)NativeJournal.Freeze(once,new Dictionary<string,object>{{"schemaVersion",1},{"qualificationScope",Scope},{"openRequestId",args["openRequestId"]},{"openReceiptSha256",args["openReceiptSha256"]},{"openPlanSha256",args["openPlanSha256"]},{"adoptionId",id},{"adoptionSha256",sha},{"copyId",copy},{"ownershipSha256",owner},{"noCommandReplay",true}});
     result["state"]=frozen["state"];result["adoptionId"]=id;result["adoptionSha256"]=sha;result["adoption"]=frozen;result["copyId"]=copy;result["ownershipSha256"]=owner;result["expiresUtc"]=args["expiresUtc"];result["nativeProviderQualified"]=native;result["timestampUtc"]=frozen["timestampUtc"];return result;
    }
   }
  }
 }
}
