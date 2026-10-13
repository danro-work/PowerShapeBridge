using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Explicitly marked isolated request-driven fixture. Only fake CAD contexts.
 internal static class CheckpointRecoveryWireFixture {
  static readonly string[] Faults={"control","lost_return_after_effect","no_effect","changed_process","partial_open_journal","wrong_verify_model","lost_reply"};
  static Dictionary<string,object> Read(string path){OwnedCopies.NoReparse(path);return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,object> Clone(Dictionary<string,object> data){return OwnedSavedReopenExecutionTests.Clone(data);}
  internal static Dictionary<string,object> Seed(string fault) {
   Require(File.Exists(Path.Combine(Program.Root,".checkpoint-recovery-fixture")),"Explicit isolated stage marker required; no CAD.");Require(Faults.Contains(fault),"Unknown wire fixture fault.");
   var f=new OwnedOperationPlanTests.Fixture(true,true);string root=f.Root,originId=Guid.NewGuid().ToString("N"),copyId=Path.GetFileName(Path.GetDirectoryName(f.Working));
   var owner=Read(Path.Combine(root,"workingcopies",copyId,"ownership.json"));string ownerSha=Program.Hash(Path.Combine(root,"workingcopies",copyId,"ownership.json"));
   var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",originId},{"operation","open_working_copy"},{"copyId",copyId},{"ownershipSha256",ownerSha}};
   Directory.CreateDirectory(Path.Combine(root,"copyopenplans"));string planSha=NativeJournal.Freeze(Path.Combine(root,"copyopenplans",originId+".json"),plan);
   var journal=new NativeJournal(root,originId,planSha);foreach(string state in new[]{"prepared","execution_claimed","command_started"})journal.Append(state,new Dictionary<string,object>());
   NativeJournal.Freeze(Path.Combine(Path.GetDirectoryName(f.Working),"open-once.json"),new Dictionary<string,object>{{"operationId",originId},{"planSha256",planSha},{"copyId",copyId},{"ownershipSha256",ownerSha},{"automaticReplayForbidden",true}});
   var protectedHashes=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));
   File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(root,"PSBridge.Worker.exe"));File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(root,"bridge.config.json"));
   var marker=new Dictionary<string,object>{{"schema","psbridge.checkpoint-wire-fixture.v1"},{"fault",fault},{"originId",originId},{"originPlanSha256",planSha},{"originCopyId",copyId},{"protectedHashes",protectedHashes},{"cadConnectionAttempted",false}};
   NativeJournal.Freeze(Path.Combine(root,".checkpoint-wire-fixture.json"),marker);
   Directory.CreateDirectory(Path.Combine(root,"wire-contexts"));NativeJournal.Freeze(Path.Combine(root,"wire-contexts","000000.json"),new Dictionary<string,object>{{"snapshot",Clone(f.Snapshot)},{"listing",Clone(f.Listing)},{"commandCalls",0}});
   marker["root"]=root;marker["state"]="checkpoint_wire_fixture_prepared";return marker;
  }
  internal static Dictionary<string,object> Run(string id) {
   Require(EntitySets.Hex(id,32),"Invalid fixture request ID.");string markerPath=Path.Combine(Program.Root,".checkpoint-wire-fixture.json");Require(File.Exists(markerPath),"Own wire fixture marker required; no CAD.");
   var marker=Read(markerPath);Require(Equals(marker["schema"],"psbridge.checkpoint-wire-fixture.v1")&&Equals(marker["cadConnectionAttempted"],false)&&Faults.Contains((string)marker["fault"]),"Invalid fixture marker.");
   string contexts=Path.Combine(Program.Root,"wire-contexts");string[] contextFiles=Directory.GetFiles(contexts,"*.json").OrderBy(path=>path,StringComparer.Ordinal).ToArray();Require(contextFiles.Length>0&&contextFiles.Length<32,"Fixture context sequence limit.");var saved=Read(contextFiles.Last());var context=new OwnedSavedReopenExecutionTests.Context{Snapshot=(Dictionary<string,object>)saved["snapshot"],Listing=(Dictionary<string,object>)saved["listing"],Commands=(int)saved["commandCalls"]};
   string stamp=DateTime.UtcNow.ToString("o");context.Snapshot["timestampUtc"]=stamp;context.Listing["timestampUtc"]=stamp;
   var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing")));QueueOperations.ValidateRequest(id,request);Require(Equals(request["operation"],"recipe.prepare"),"Only fixed recipes supported.");
   var envelope=(Dictionary<string,object>)request["arguments"];string recipe=(string)envelope["recipeId"];var args=(Dictionary<string,object>)envelope["parameters"];Dictionary<string,object> result;
   if(recipe=="prepare_checkpoint_recovery")result=CheckpointRecovery.Prepare(Program.Root,id,args);
   else if(recipe=="create_checkpoint_working_copy")result=CheckpointRecovery.Create(Program.Root,id,args);
   else if(recipe=="inspect_active_model"){Require(args.Count==0,"Unexpected read parameters.");result=context.Observation();}
   else if(recipe=="list_documents") {
    Require(args.Count==0,"Unexpected listing parameters.");result=context.Windows();result["state"]="documents_listed_verified";
    result["documents"]=WorkingCopyOpen.Windows(result).Select(w=>new Dictionary<string,object>{{"modelId",w["modelId"]},{"name",w["modelName"]},{"path",w["modelPath"]},{"isActive",Equals(w["selectedRaw"],1)},{"activationSupported",true},{"windowNumber",w["windowNumber"]},{"windowNumbers",new[]{w["windowNumber"]}}}).ToArray();
   } else if(recipe=="open_working_copy") {
    string fault=(string)marker["fault"];context.ThrowAfter=fault=="lost_return_after_effect";context.SuppressOpen=fault=="no_effect";
    if(fault=="changed_process")context.DuringOpen=path=>{context.Snapshot["processId"]=99;context.Listing["processId"]=99;};
    if(fault=="partial_open_journal")context.DuringOpen=path=>File.WriteAllText(Path.Combine(Program.Root,"operations",id,"writer.lock"),"Own interrupted fixture publication.");
    var snapshot=EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]);var listing=EntitySets.Receipt(Program.Root,(string)args["listingRequestId"],(string)args["listingSha256"]);
    result=Clone(WorkingCopyOpen.Execute(Program.Root,id,args,listing,snapshot,context));
    if(fault=="wrong_verify_model") {
     var windows=WorkingCopyOpen.Windows(context.Listing);foreach(var w in windows)w["selectedRaw"]=Equals(w["windowNumber"],1)?1:0;var old=windows.Single(w=>Equals(w["windowNumber"],1));context.Listing["activeWindow"]=old;
     context.Snapshot["model"]=new Dictionary<string,object>{{"id",old["modelId"]},{"name",old["modelName"]},{"path",old["modelPath"]}};
    }
   } else if(recipe=="verify_working_copy_open")result=OwnedOpenVerification.Verify(Program.Root,args,()=>context);
   else if(recipe=="bind_working_document")result=DocumentBindings.Prepare(Program.Root,id,args,EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]),context,new Dictionary<string,object>());
   else throw new ArgumentException("Fixture recipe is not supported.");
   Require(context.Commands<=1,"Fixture attempted repeated OPEN.");
   NativeJournal.Freeze(Path.Combine(contexts,contextFiles.Length.ToString("D6")+".json"),new Dictionary<string,object>{{"snapshot",context.Snapshot},{"listing",context.Listing},{"commandCalls",context.Commands}});
   result["bridgeRequestId"]=id;return result;
  }
 }
}
