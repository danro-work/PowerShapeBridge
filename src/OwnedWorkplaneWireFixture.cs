using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneWireFixture {
  static void Need(bool ok,string reason){OwnedWorkplanePlans.Need(ok,reason);}
  static Dictionary<string,object> Read(string path){OwnedCopies.NoReparse(path);return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  internal static Dictionary<string,object> Admission() {
   var original=Read(Path.Combine(Program.Root,"bridge.config.json"));var configs=new List<object>();
   foreach(string fault in new[]{"true","false","string","integer","null","no_general","missing_sets","sets_string","sets_false","missing_copies","copies_false","bindings_false"}) {
    var config=OwnedWorkplanePlans.Clone(original);config["ownedWorkplanesExperimental"]=fault=="false"?(object)false:true;
    if(fault=="string")config["ownedWorkplanesExperimental"]="true";if(fault=="integer")config["ownedWorkplanesExperimental"]=1;if(fault=="null")config["ownedWorkplanesExperimental"]=null;
    if(fault=="no_general")config["coreProfile"]="fixture";if(fault=="missing_sets")config.Remove("entitySetsExperimental");if(fault=="sets_string")config["entitySetsExperimental"]="true";if(fault=="sets_false")config["entitySetsExperimental"]=false;
    if(fault=="missing_copies")config.Remove("workingCopiesExperimental");if(fault=="copies_false")config["workingCopiesExperimental"]=false;if(fault=="bindings_false")config["documentBindingsExperimental"]=false;
    bool accepted=true;try{Program.ParseConfig(Program.Json.Serialize(config));}catch(ArgumentException){accepted=false;}
    Need(accepted==new[]{"true","false"}.Contains(fault),"Workplane native configuration admission mismatch.");configs.Add(new{fault=fault,accepted=accepted});
   }
   var rows=new List<object>();bool enabled=Program.ReadConfig().ownedWorkplanesExperimental;
   foreach(string recipe in OwnedWorkplaneOperations.Recipes)foreach(string fault in new[]{"control","extra_outer","extra_parameters","missing_parameters","bad_identity"}) {
    string id=Guid.NewGuid().ToString("N"),token=new string('a',32),sha=new string('b',64);Dictionary<string,object> args;
    if(recipe=="prepare_owned_workplane")args=new Dictionary<string,object>{{"bindingId",token},{"bindingSha256",sha},{"parameters",OwnedWorkplaneTests.Parameters()}};
    else if(recipe=="execute_owned_workplane")args=new Dictionary<string,object>{{"ownedWorkplaneId",token},{"ownedWorkplaneSha256",sha}};
    else args=new Dictionary<string,object>{{"executionRequestId",token},{"executionReceiptSha256",sha},{"inspectionRequestId",token},{"inspectionSha256",sha},{"listingRequestId",token},{"listingSha256",sha},{"expiresUtc",DateTime.UtcNow.AddSeconds(120).ToString("o")},{"windowNumber",1},{"modelId",9}};
    if(fault=="extra_parameters")args["command"]="CREATE";if(fault=="bad_identity")args[recipe=="prepare_owned_workplane"?"bindingId":recipe=="execute_owned_workplane"?"ownedWorkplaneId":"executionRequestId"]="../unsafe";
    var envelope=new Dictionary<string,object>{{"recipeId",recipe},{"parameters",args}};if(fault=="missing_parameters")envelope.Remove("parameters");var request=new Dictionary<string,object>{{"requestId",id},{"operation","recipe.prepare"},{"arguments",envelope}};if(fault=="extra_outer")request["unsafe"]=true;
    bool accepted=true;try{QueueOperations.ValidateRequest(id,request);}catch(ArgumentException){accepted=false;}catch(InvalidDataException){accepted=false;}
    Need(accepted==(fault=="control"&&enabled),"Workplane native request admission mismatch.");rows.Add(new{recipe=recipe,fault=fault,accepted=accepted});
   }
   return new Dictionary<string,object>{{"state","passed"},{"enabled",enabled},{"configurations",configs.ToArray()},{"requests",rows.ToArray()},{"cadConnectionAttempted",false}};
  }
  internal static Dictionary<string,object> Seed() {
   Need(File.Exists(Path.Combine(Program.Root,".owned-workplane-wire-fixture")),"Explicit isolated workplane wire marker required; no CAD.");
   var f=new OwnedOperationPlanTests.Fixture(true,true);f.Snapshot["busy"]=false;f.Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","General"},{"activeRaw",1}}};
   var protectedHashes=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));
   File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(f.Root,"PSBridge.Worker.exe"));File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(f.Root,"bridge.config.json"));
   var marker=new Dictionary<string,object>{{"schema","psbridge.owned-workplane-wire.v1"},{"cadConnectionAttempted",false},{"sourcePath",Path.Combine(f.Root,"Source.psmodel")},{"protectedHashes",protectedHashes}};
   NativeJournal.Freeze(Path.Combine(f.Root,".owned-workplane-wire.json"),marker);Directory.CreateDirectory(Path.Combine(f.Root,"wire-contexts"));
   NativeJournal.Freeze(Path.Combine(f.Root,"wire-contexts","000000.json"),new Dictionary<string,object>{{"snapshot",f.Snapshot},{"listing",f.Listing},{"creates",0},{"activations",0},{"active",0},{"frame",null}});marker["root"]=f.Root;return marker;
  }
  internal static Dictionary<string,object> Run(string id) {
   Need(EntitySets.Hex(id,32),"Invalid workplane wire request.");var marker=Read(Path.Combine(Program.Root,".owned-workplane-wire.json"));Need(Equals(marker["schema"],"psbridge.owned-workplane-wire.v1")&&Equals(marker["cadConnectionAttempted"],false),"Invalid isolated workplane wire marker.");
   string folder=Path.Combine(Program.Root,"wire-contexts");var files=Directory.GetFiles(folder,"*.json").OrderBy(p=>p,StringComparer.Ordinal).ToArray();Need(files.Length>0&&files.Length<64,"Wire context limit.");var saved=Read(files.Last());
   var context=new OwnedWorkplaneTests.Context((Dictionary<string,object>)saved["snapshot"],(Dictionary<string,object>)saved["listing"]){Creates=(int)saved["creates"],Activations=(int)saved["activations"],Active=(int)saved["active"]};
   if(saved["frame"]!=null)context.Frame=((IEnumerable)saved["frame"]).Cast<object>().Select(Program.StrictDouble).ToArray();
   context.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");
   var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing")));QueueOperations.ValidateRequest(id,request);Need(Equals(request["operation"],"recipe.prepare"),"Fixed recipes required.");
   var envelope=(Dictionary<string,object>)request["arguments"];var args=(Dictionary<string,object>)envelope["parameters"];string recipe=(string)envelope["recipeId"];Dictionary<string,object> result;
   if(recipe=="prepare_working_copy") {
    result=OwnedCopies.Create(Program.Root,id,(string)args["sourcePath"],(string)args["sourceSha256"]);var owner=(Dictionary<string,object>)result["ownership"];string path=(string)((Dictionary<string,object>)owner["working"])["path"];
    var model=(Dictionary<string,object>)context.Snapshot["model"];model["path"]=path;model["name"]=Path.GetFileNameWithoutExtension(path);
    var windows=WorkingCopyOpen.Windows(context.Listing);foreach(var window in windows){window["modelPath"]=path;window["modelName"]=model["name"];}context.Listing["windows"]=windows;context.Listing["activeWindow"]=windows.Single();
   }else if(recipe=="inspect_active_model")result=context.Observation();
   else if(recipe=="list_documents") {result=context.Windows();result["state"]="documents_listed_verified";var document=new Dictionary<string,object>((Dictionary<string,object>)result["activeWindow"]);document["activationSupported"]=true;document["isActive"]=true;result["documents"]=new[]{document};}
   else if(recipe=="bind_working_document")result=DocumentBindings.Prepare(Program.Root,id,args,EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]),context,new Dictionary<string,object>());
   else if(recipe=="prepare_owned_workplane")result=OwnedWorkplanePlans.Check(Program.Root,id,args,true,context);
   else if(recipe=="execute_owned_workplane")result=OwnedWorkplaneExecution.Execute(Program.Root,id,args,context);
   else if(recipe=="verify_owned_workplane")result=OwnedWorkplaneExecution.Verify(Program.Root,args,context);
   else throw new ArgumentException("Unsupported workplane wire recipe.");
   NativeJournal.Freeze(Path.Combine(folder,files.Length.ToString("D6")+".json"),new Dictionary<string,object>{{"snapshot",context.Snapshot},{"listing",context.Listing},{"creates",context.Creates},{"activations",context.Activations},{"active",context.Active},{"frame",context.Frame}});
   result["bridgeRequestId"]=id;if(!result.ContainsKey("timestampUtc"))result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
