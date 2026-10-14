using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Request-driven isolated transport fixture. Never constructs a COM context.
 internal static class OwnedDgkTargetWireFixture {
  static readonly string[] Faults={"control","source_tamper","fenced_copy","lost_reply","target_tamper","directory_replaced"};
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,object> Read(string path){OwnedCopies.NoReparse(path);return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  internal static Dictionary<string,object> Seed(string fault) {
   Require(File.Exists(Path.Combine(Program.Root,".owned-dgk-wire-fixture")),"Explicit isolated DGK stage marker required; no CAD.");Require(Faults.Contains(fault),"Unknown DGK wire fault.");
   var f=new OwnedOperationPlanTests.Fixture(true,true);string root=f.Root;
   var hashes=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));
   File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(root,"PSBridge.Worker.exe"));File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(root,"bridge.config.json"));
   var marker=new Dictionary<string,object>{{"schema","psbridge.dgk-target-wire-fixture.v1"},{"fault",fault},{"protectedHashes",hashes},{"cadConnectionAttempted",false},{"sourcePath",Path.Combine(root,"Source.psmodel")}};
   NativeJournal.Freeze(Path.Combine(root,".dgk-target-wire-fixture.json"),marker);Directory.CreateDirectory(Path.Combine(root,"wire-contexts"));
   NativeJournal.Freeze(Path.Combine(root,"wire-contexts","000000.json"),new Dictionary<string,object>{{"snapshot",f.Snapshot},{"listing",f.Listing},{"commandCalls",0}});
   marker["root"]=root;return marker;
  }
  internal static Dictionary<string,object> Run(string id) {
   Require(EntitySets.Hex(id,32),"Invalid fixture request ID.");string markerPath=Path.Combine(Program.Root,".dgk-target-wire-fixture.json");Require(File.Exists(markerPath),"Own DGK wire marker required; no CAD.");var marker=Read(markerPath);
   Require(Equals(marker["schema"],"psbridge.dgk-target-wire-fixture.v1")&&Equals(marker["cadConnectionAttempted"],false)&&Faults.Contains((string)marker["fault"]),"Invalid fixture marker.");
   string contexts=Path.Combine(Program.Root,"wire-contexts");var files=Directory.GetFiles(contexts,"*.json").OrderBy(path=>path,StringComparer.Ordinal).ToArray();Require(files.Length>0&&files.Length<16,"Fixture context sequence limit.");var saved=Read(files.Last());Require(Equals(saved["commandCalls"],0),"No CAD command permitted.");
   var context=new OwnedOperationPlanTests.Context{Snapshot=(Dictionary<string,object>)saved["snapshot"],Listing=(Dictionary<string,object>)saved["listing"]};string stamp=DateTime.UtcNow.ToString("o");context.Snapshot["timestampUtc"]=stamp;
   var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing")));QueueOperations.ValidateRequest(id,request);Require(Equals(request["operation"],"recipe.prepare"),"Only fixed recipes supported.");var envelope=(Dictionary<string,object>)request["arguments"];string recipe=(string)envelope["recipeId"];var args=(Dictionary<string,object>)envelope["parameters"];Dictionary<string,object> result;
   if(recipe=="prepare_working_copy") {
    result=OwnedCopies.Create(Program.Root,id,(string)args["sourcePath"],(string)args["sourceSha256"]);
    var owner=(Dictionary<string,object>)result["ownership"];string working=(string)((Dictionary<string,object>)owner["working"])["path"];
    // Simulate an already-open owned document; not an OPEN qualification or CAD action.
    var model=(Dictionary<string,object>)context.Snapshot["model"];model["path"]=working;model["name"]=Path.GetFileNameWithoutExtension(working);
    var windows=WorkingCopyOpen.Windows(context.Listing);foreach(var window in windows){window["modelPath"]=working;window["modelName"]=model["name"];}
    context.Listing["windows"]=windows;context.Listing["activeWindow"]=windows.Single();
   }else if(recipe=="inspect_active_model"){Require(args.Count==0,"Unexpected read parameters.");result=context.Observation();}
   else if(recipe=="prepare_entity_set")result=EntitySets.InContext(id,args,true,context.Observation);
   else if(recipe=="bind_working_document")result=DocumentBindings.Prepare(Program.Root,id,args,EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]),context,new Dictionary<string,object>());
   else if(recipe=="prepare_owned_dgk_export") {
    string fault=(string)marker["fault"];
    if(fault=="source_tamper")File.AppendAllText(Path.Combine(Program.Root,"entitysets",(string)args["entitySetId"]+".json")," ");
    if(fault=="fenced_copy"){var binding=Read(Path.Combine(Program.Root,"documentbindings",(string)args["bindingId"]+".json"));NativeJournal.Freeze(Path.Combine(Program.Root,"workingcopies",(string)binding["copyId"],"mutation-fence.json"),new Dictionary<string,object>{{"fixtureOnly",true}});}
    // This dispatch is the actual production filesystem recipe. It returns
    // before any COM branch; prior reads above use explicitly fake context.
    result=QueueOperations.Run(id);
    if(fault=="target_tamper"&&result.ContainsKey("targetSha256"))File.AppendAllText(Path.Combine(OwnedDgkTargets.Folder(Program.Root,id),"target.json")," ");
    if(fault=="directory_replaced"&&result.ContainsKey("targetSha256")){string path=OwnedDgkTargets.BundleFolder(Program.Root,id);Directory.Move(path,Path.Combine(OwnedDgkTargets.Folder(Program.Root,id),"retained"));Directory.CreateDirectory(path);}
   }else throw new ArgumentException("Fixture recipe is not supported.");
   NativeJournal.Freeze(Path.Combine(contexts,files.Length.ToString("D6")+".json"),new Dictionary<string,object>{{"snapshot",context.Snapshot},{"listing",context.Listing},{"commandCalls",0},{"observationReads",context.Reads}});result["bridgeRequestId"]=id;if(!result.ContainsKey("timestampUtc"))result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
